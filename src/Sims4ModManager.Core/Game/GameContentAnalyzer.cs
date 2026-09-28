using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Game;

/// <summary>What kind of game content a mod replaces.</summary>
public enum ReplacementKind
{
    CasPart,
    Object,
    Build,
    Mesh,
    Texture,
    Slider,
    /// <summary>Tuning or its SimData - breaks most easily with game updates.</summary>
    Tuning,
    Other
}

/// <summary>A mod file that replaces resources of the game itself (default replacement or tuning override).</summary>
public sealed record GameReplacement(
    ModEntry Mod, ModFileInfo File, IReadOnlyDictionary<ReplacementKind, int> Counts, IReadOnlyList<string> TuningNames)
{
    public bool TouchesTuning => Counts.ContainsKey(ReplacementKind.Tuning);
    public int Total => Counts.Values.Sum();

    public string Summary => string.Join(", ", Counts.OrderBy(c => c.Key).Select(c => $"{c.Value} × {GameContentAnalyzer.Label(c.Key)}"));
}

public enum MeshStatus
{
    /// <summary>Neither a mod nor the game provides the mesh: the item is invisible in the game.</summary>
    Missing,
    /// <summary>The mesh is in a disabled mod.</summary>
    Disabled
}

/// <summary>A recolor whose mesh is missing (or only in a disabled mod).</summary>
public sealed record MissingMesh(ModEntry Mod, ModFileInfo File, string Item, ResourceKey Mesh, MeshStatus Status, ModEntry? DisabledProvider);

/// <summary>Compares the mods with the <see cref="GameIndex"/>.</summary>
public static partial class GameContentAnalyzer
{
    private static readonly HashSet<uint> CasTypes = new() { 0x034AEECB, 0x0354796A };
    private static readonly HashSet<uint> ObjectTypes = new() { 0xC0DB5AE7, 0x319E4F1D };
    private static readonly HashSet<uint> MeshTypes = new() { 0x015A1849, 0x01661233, 0x01D10F34, 0x01D0E75D, 0x8EAF13DE, 0xD3044521, 0xD382BF57 };
    private static readonly HashSet<uint> TextureTypes = new() { 0x00B2D882, 0x3453CF95, 0xBA856C78, 0xAC16FBEC };
    private static readonly HashSet<uint> ExtraSliderTypes = new() { 0x8B18FF6E, 0xEAA32ADD };
    private const uint SimData = 0x545AC67A;

    /// <summary>Replacing these says nothing (thumbnails come with every replacement, the manifest is S4S bookkeeping).</summary>
    private static readonly HashSet<uint> Ignored = new() { 0x3C1AF1F2, 0x3C2A8647, 0x5B282D45, 0x7FB6AD8A, 0x220557DA, 0x0166038C };

    [GeneratedRegex(@"<I\s[^>]*?\bn=""(?<n>[^""]+)""[^>]*?\bs=""(?<s>\d+)""")]
    private static partial Regex TuningHeader();

    /// <summary>EA names tuning "class_Name…" with a lower-case class prefix; creators mostly prefix their own name.</summary>
    [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*_")]
    private static partial Regex EaStyleName();

    public static string Label(ReplacementKind kind) => kind switch
    {
        ReplacementKind.CasPart => L.T("CAS-Teile"),
        ReplacementKind.Object => L.T("Objekte"),
        ReplacementKind.Build => L.T("Bauelemente"),
        ReplacementKind.Mesh => L.T("Meshes"),
        ReplacementKind.Texture => L.T("Texturen"),
        ReplacementKind.Slider => L.T("Slider"),
        ReplacementKind.Tuning => L.T("Tuning"),
        _ => L.T("Sonstiges")
    };

    public static ReplacementKind KindOf(uint type)
    {
        if (CasTypes.Contains(type)) return ReplacementKind.CasPart;
        if (ObjectTypes.Contains(type)) return ReplacementKind.Object;
        if (ModClassifier.BuildTypes.Contains(type)) return ReplacementKind.Build;
        if (MeshTypes.Contains(type)) return ReplacementKind.Mesh;
        if (TextureTypes.Contains(type)) return ReplacementKind.Texture;
        if (ModClassifier.SliderTypes.Contains(type) || ExtraSliderTypes.Contains(type)) return ReplacementKind.Slider;
        if (type == SimData) return ReplacementKind.Tuning;
        return ReplacementKind.Other;
    }

    /// <summary>
    /// Mod files that replace game resources. Exact key matches with the game index are certain. XML
    /// tuning cannot be matched that way (the game keeps it in one combined block), so tuning counts as
    /// an override when it carries an EA-style id (below 2^32) and an EA-style name - creators' own
    /// tuning gets 64-bit hash ids.
    /// </summary>
    public static IReadOnlyList<GameReplacement> FindReplacements(IEnumerable<ModEntry> mods, GameIndex game, CancellationToken cancel = default)
    {
        var files = mods.SelectMany(m => m.Files.Where(f => f.Kind == ModFileKind.Package).Select(f => (Mod: m, File: f))).ToList();
        var result = new ConcurrentBag<GameReplacement>();
        Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = 4 }, item =>
        {
            var counts = new Dictionary<ReplacementKind, int>();
            var tuningCandidates = new List<PackageResource>();
            foreach (var resource in item.File.Resources)
            {
                uint type = resource.Key.Type;
                if (Ignored.Contains(type))
                    continue;
                if (game.Contains(resource.Key))
                {
                    var kind = KindOf(type);
                    counts[kind] = counts.GetValueOrDefault(kind) + 1;
                }
                else if (IsTuningCandidate(resource))
                {
                    tuningCandidates.Add(resource);
                }
            }

            var tuningNames = new List<string>();
            if (tuningCandidates.Count > 0)
            {
                foreach (var (_, data) in DbpfReader.TryReadResources(item.File.AbsolutePath, tuningCandidates))
                {
                    var match = TuningHeader().Match(Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 1024)));
                    if (match.Success && EaStyleName().IsMatch(match.Groups["n"].Value))
                        tuningNames.Add(match.Groups["n"].Value);
                }
                if (tuningNames.Count > 0)
                    counts[ReplacementKind.Tuning] = counts.GetValueOrDefault(ReplacementKind.Tuning) + tuningNames.Count;
            }

            if (counts.Count > 0)
                result.Add(new GameReplacement(item.Mod, item.File, counts, tuningNames.Order(StringComparer.Ordinal).ToList()));
        });
        return result.OrderBy(r => r.Mod.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.File.RelativePathInMod, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool IsTuningCandidate(PackageResource r) =>
        r.Key.Group == 0 && r.Key.Instance is > 0 and <= uint.MaxValue && r.MemorySize is > 0 and < 4_000_000
        && KindOf(r.Key.Type) == ReplacementKind.Other && !IsKnownAsset(r.Key.Type);

    private static bool IsKnownAsset(uint type) =>
        ResourceTypeCatalog.Describe(type).Severity != ConflictSeverity.High && ResourceTypeCatalog.IsKnown(type);

    /// <summary>
    /// Recolors whose mesh neither a mod nor the game provides - "invisible CC" - or that only a disabled mod provides.
    /// </summary>
    public static IReadOnlyList<MissingMesh> FindMissingMeshes(
        IEnumerable<ModEntry> mods, Func<ModFileInfo, PackageCatalogInfo?> catalog, GameIndex game)
    {
        var modList = mods.ToList();
        var providers = new Dictionary<ResourceKey, (ModEntry Mod, bool Enabled)>();
        foreach (var mod in modList)
        {
            foreach (var file in mod.Files)
            {
                foreach (var resource in file.Resources)
                {
                    if (resource.Key.Type is not (MeshReferenceReader.Geometry or MeshReferenceReader.Model))
                        continue;
                    if (!providers.TryGetValue(resource.Key, out var existing) || (!existing.Enabled && file.IsEnabled))
                        providers[resource.Key] = (mod, file.IsEnabled);
                }
            }
        }

        var result = new List<MissingMesh>();
        foreach (var mod in modList)
        {
            foreach (var file in mod.Files.Where(f => f.IsEnabled))
            {
                var info = catalog(file);
                if (info is null)
                    continue;
                foreach (var external in info.ExternalMeshes)
                {
                    var keys = external.ParsedKeys.ToList();
                    if (keys.Count == 0 || keys.Any(game.Contains))
                        continue;
                    var found = keys.Select(k => providers.TryGetValue(k, out var p) ? p : ((ModEntry, bool)?)null).OfType<(ModEntry Mod, bool Enabled)>().ToList();
                    if (found.Any(p => p.Enabled))
                        continue;
                    result.Add(found.Count > 0
                        ? new MissingMesh(mod, file, external.Item, keys[0], MeshStatus.Disabled, found[0].Mod)
                        : new MissingMesh(mod, file, external.Item, keys[0], MeshStatus.Missing, null));
                }
            }
        }
        return result;
    }
}
