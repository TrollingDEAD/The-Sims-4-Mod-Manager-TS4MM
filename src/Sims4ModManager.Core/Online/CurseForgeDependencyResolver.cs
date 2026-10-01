namespace Sims4ModManager.Core.Online;

/// <summary>One file to install: a root (explicitly requested) or a dependency pulled in automatically.</summary>
public sealed record CurseForgeDependencyEntry(bool IsRoot, long ModId, CurseForgeFile File);

public sealed record CurseForgeDependencyResult(IReadOnlyList<CurseForgeDependencyEntry> Chain, IReadOnlyList<CurseForgeMod> Blocked);

/// <summary>
/// Walks a CurseForge mod's (or several mods') required/optional dependencies to build the full set
/// of files an install needs, deduplicated by mod ID across every root - two requested mods sharing a
/// dependency only download and install it once. A mod that disallows third-party distribution ends
/// up in <see cref="CurseForgeDependencyResult.Blocked"/> instead of the chain, for the caller to warn
/// about and refuse rather than silently skip.
/// </summary>
public static class CurseForgeDependencyResolver
{
    private const int MaxDependencyDepth = 5;

    /// <summary>
    /// <paramref name="roots"/> are installed at their exact given file, regardless of whether a newer
    /// one exists - e.g. a modpack pins specific versions. Dependencies instead always resolve to each
    /// dependency mod's current best file (see <see cref="PickBestFile"/>).
    /// </summary>
    public static async Task<CurseForgeDependencyResult> ResolveAsync(
        CurseForgeClient client, IReadOnlyList<(CurseForgeMod Mod, CurseForgeFile File)> roots, CancellationToken cancel = default)
    {
        var chain = new List<CurseForgeDependencyEntry>();
        var blocked = new List<CurseForgeMod>();
        var visited = new HashSet<long>();
        var frontier = new List<long>();

        foreach (var (mod, file) in roots)
        {
            if (!visited.Add(mod.Id))
                continue; // listed as a root more than once
            // Always re-fetch the chosen file's full shape: search/list responses may carry a thinner
            // file object than a direct file lookup, and Dependencies is only reliably populated there.
            var full = (await client.GetFilesAsync(new[] { file.Id }, cancel)).FirstOrDefault() ?? file;
            chain.Add(new CurseForgeDependencyEntry(true, mod.Id, full));
            if (mod.AllowModDistribution == false)
                blocked.Add(mod);
            frontier.AddRange(RequiredOrOptionalIds(full.Dependencies).Where(visited.Add));
        }

        for (int depth = 0; depth < MaxDependencyDepth && frontier.Count > 0; depth++)
        {
            var mods = await client.GetModsAsync(frontier, cancel);
            var chosen = mods.Select(m => (Mod: m, File: PickBestFile(m.LatestFiles))).Where(x => x.File is not null).ToList();
            if (chosen.Count == 0)
                break;
            var fullFiles = (await client.GetFilesAsync(chosen.Select(x => x.File!.Id).ToList(), cancel)).ToDictionary(f => f.Id);

            var nextFrontier = new List<long>();
            foreach (var (mod, file) in chosen)
            {
                var full = fullFiles.GetValueOrDefault(file!.Id, file);
                chain.Add(new CurseForgeDependencyEntry(false, mod.Id, full));
                if (mod.AllowModDistribution == false)
                    blocked.Add(mod);
                nextFrontier.AddRange(RequiredOrOptionalIds(full.Dependencies).Where(visited.Add));
            }
            frontier = nextFrontier;
        }
        return new CurseForgeDependencyResult(chain, blocked);
    }

    /// <summary>Which dependency relation types get auto-installed: Required (3) and Optional (2) only -
    /// never EmbeddedLibrary (already inside the file), Tool, Incompatible or Include.</summary>
    internal static IEnumerable<long> RequiredOrOptionalIds(IEnumerable<CurseForgeFileDependency> dependencies) =>
        dependencies.Where(d => d.RelationType is 2 or 3).Select(d => d.ModId);

    /// <summary>Release beats beta/alpha, else newest - the same rule CurseForgeUpdateChecker uses for updates.</summary>
    public static CurseForgeFile? PickBestFile(IReadOnlyList<CurseForgeFile> files)
    {
        var available = files.Where(f => f.IsAvailable).ToList();
        return available.Where(f => f.ReleaseType == 1).OrderByDescending(f => f.FileDate).FirstOrDefault()
            ?? available.OrderByDescending(f => f.FileDate).FirstOrDefault();
    }
}
