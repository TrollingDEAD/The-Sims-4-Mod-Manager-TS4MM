using System.Collections.Concurrent;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core;

/// <summary>
/// Hashes the given resources of one package file (see <see cref="DbpfReader.TryHashResources"/> for
/// the meaning of <paramref name="decompress"/>). Resources that cannot be read map to null (or are
/// missing from the result) and are then treated as "possibly different". Called concurrently for
/// different files.
/// </summary>
public delegate IReadOnlyDictionary<PackageResource, string?> ResourceHasher(
    string packagePath, IReadOnlyCollection<PackageResource> resources, bool decompress);

/// <summary>
/// Finds resource keys and script modules provided by two or more distinct mods through enabled files.
/// Duplicates within the same mod (common for legitimate multi-file packages) are not reported, and
/// neither are duplicates whose content is byte-identical in every mod - only differing cross-mod
/// overrides are actual "one version wins" conflicts in-game. Results are grouped by the set of
/// mods involved.
/// </summary>
public static class ConflictDetector
{
    private sealed record Occurrence<T>(ModEntry Mod, ModFileInfo File, T Item);

    public static ConflictReport FindConflicts(IEnumerable<ModEntry> mods, ResourceHasher? hasher = null)
    {
        hasher ??= DbpfReader.TryHashResources;

        var resourceOccurrences = new Dictionary<ResourceKey, List<Occurrence<PackageResource>>>();
        var scriptOccurrences = new Dictionary<string, List<Occurrence<ScriptModule>>>(StringComparer.Ordinal);
        var unreadable = new List<UnreadableModFile>();

        // Look at enabled files, not just fully enabled mods: a partially enabled mod still loads
        // its enabled files in-game and can therefore conflict.
        foreach (var mod in mods)
        {
            foreach (var file in mod.Files.Where(f => f.IsEnabled))
            {
                if (file.IsUnreadable)
                {
                    unreadable.Add(new UnreadableModFile(mod, file));
                    continue;
                }

                if (file.Kind == ModFileKind.Package)
                {
                    foreach (var resource in file.Resources.Where(r => !ResourceTypeCatalog.IsIgnoredForConflicts(r.Key.Type)))
                        Add(resourceOccurrences, resource.Key, new Occurrence<PackageResource>(mod, file, resource));
                }
                else if (file.Kind == ModFileKind.Script)
                {
                    foreach (var module in file.ScriptModules)
                        Add(scriptOccurrences, module.ModulePath, new Occurrence<ScriptModule>(mod, file, module));
                }
            }
        }

        var resourceCandidates = resourceOccurrences.Where(kv => SpansMultipleMods(kv.Value)).ToList();
        var scriptCandidates = scriptOccurrences.Where(kv => SpansMultipleMods(kv.Value)).ToList();

        var differingKeys = FindDifferingResources(resourceCandidates, hasher);

        var conflicts = new List<(ConflictItem Item, IReadOnlyList<ModEntry> Mods)>();
        int ignoredIdentical = 0;

        foreach (var (key, occurrences) in resourceCandidates)
        {
            if (!differingKeys.Contains(key))
            {
                ignoredIdentical++;
                continue;
            }

            var typeInfo = ResourceTypeCatalog.Describe(key.Type);
            conflicts.Add((new ConflictItem
            {
                Kind = ConflictItemKind.Resource,
                Key = key,
                TypeName = typeInfo.Name,
                Severity = typeInfo.Severity,
                Sources = Sources(occurrences)
            }, DistinctMods(occurrences)));
        }

        foreach (var (modulePath, occurrences) in scriptCandidates)
        {
            if (AllIdentical(occurrences.Select(o => o.Item.Fingerprint)))
            {
                ignoredIdentical++;
                continue;
            }

            conflicts.Add((new ConflictItem
            {
                Kind = ConflictItemKind.ScriptModule,
                ModulePath = modulePath,
                TypeName = "Python-Modul",
                Severity = ConflictSeverity.High,
                Sources = Sources(occurrences)
            }, DistinctMods(occurrences)));
        }

        var groups = conflicts
            .GroupBy(c => string.Join("\n", c.Mods.Select(m => m.Id)), StringComparer.Ordinal)
            .Select(g => new ConflictGroup
            {
                Mods = g.First().Mods,
                Items = g.Select(c => c.Item)
                    .OrderByDescending(i => i.Severity)
                    .ThenBy(i => i.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.Label, StringComparer.Ordinal)
                    .ToList()
            })
            .OrderByDescending(g => g.Severity)
            .ThenByDescending(g => g.Items.Count)
            .ThenBy(g => g.Mods[0].DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ConflictReport
        {
            Groups = groups,
            UnreadableFiles = unreadable,
            IgnoredIdenticalCount = ignoredIdentical
        };
    }

    private static void Add<TKey, T>(Dictionary<TKey, List<Occurrence<T>>> map, TKey key, Occurrence<T> occurrence)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<Occurrence<T>>();
            map[key] = list;
        }
        list.Add(occurrence);
    }

    private static bool SpansMultipleMods<T>(List<Occurrence<T>> occurrences) =>
        occurrences.Any(o => !ReferenceEquals(o.Mod, occurrences[0].Mod));

    private static IReadOnlyList<ConflictSource> Sources<T>(IEnumerable<Occurrence<T>> occurrences) =>
        occurrences.Select(o => new ConflictSource(o.Mod, o.File)).Distinct().ToList();

    private static IReadOnlyList<ModEntry> DistinctMods<T>(IEnumerable<Occurrence<T>> occurrences) =>
        occurrences
            .Select(o => o.Mod)
            .Distinct()
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>True only if every fingerprint is known and they are all equal; unknown content counts as a conflict.</summary>
    private static bool AllIdentical(IEnumerable<string?> fingerprints)
    {
        string? first = null;
        foreach (var fingerprint in fingerprints)
        {
            if (fingerprint is null)
                return false;
            first ??= fingerprint;
            if (!string.Equals(first, fingerprint, StringComparison.Ordinal))
                return false;
        }
        return first is not null;
    }

    /// <summary>
    /// Returns the candidate keys whose content actually differs between occurrences, doing as little
    /// I/O as possible - real mod folders easily have tens of thousands of shared (mostly identical)
    /// resources whose decompressed size runs into gigabytes:
    /// 1. Different uncompressed sizes mean different content - no reading needed.
    /// 2. Otherwise compare hashes of the stored bytes - equal means identical.
    /// 3. Only if those differ, decompress and compare again (same content, compressed differently).
    /// </summary>
    private static HashSet<ResourceKey> FindDifferingResources(
        List<KeyValuePair<ResourceKey, List<Occurrence<PackageResource>>>> candidates, ResourceHasher hasher)
    {
        var differing = new HashSet<ResourceKey>();

        var sameSize = new List<KeyValuePair<ResourceKey, List<Occurrence<PackageResource>>>>();
        foreach (var candidate in candidates)
        {
            uint size = candidate.Value[0].Item.MemorySize;
            if (candidate.Value.All(o => o.Item.MemorySize == size))
                sameSize.Add(candidate);
            else
                differing.Add(candidate.Key);
        }

        var storedHashes = ComputeFingerprints(sameSize.SelectMany(kv => kv.Value), hasher, decompress: false);
        var storedDiffers = sameSize
            .Where(kv => !AllIdentical(kv.Value.Select(o => storedHashes.GetValueOrDefault((o.File.AbsolutePath, o.Item)))))
            .ToList();

        var contentHashes = ComputeFingerprints(storedDiffers.SelectMany(kv => kv.Value), hasher, decompress: true);
        foreach (var (key, occurrences) in storedDiffers)
        {
            if (!AllIdentical(occurrences.Select(o => contentHashes.GetValueOrDefault((o.File.AbsolutePath, o.Item)))))
                differing.Add(key);
        }

        return differing;
    }

    /// <summary>Hashes all given resources, opening each package file once and processing files in parallel.</summary>
    private static ConcurrentDictionary<(string Path, PackageResource Resource), string?> ComputeFingerprints(
        IEnumerable<Occurrence<PackageResource>> occurrences, ResourceHasher hasher, bool decompress)
    {
        var result = new ConcurrentDictionary<(string, PackageResource), string?>();

        var byFile = occurrences
            .GroupBy(o => o.File.AbsolutePath, o => o.Item, StringComparer.Ordinal)
            .Select(g => (Path: g.Key, Resources: g.Distinct().ToList()))
            .ToList();

        Parallel.ForEach(byFile, file =>
        {
            var hashes = hasher(file.Path, file.Resources, decompress);
            foreach (var resource in file.Resources)
                result[(file.Path, resource)] = hashes.GetValueOrDefault(resource);
        });

        return result;
    }
}
