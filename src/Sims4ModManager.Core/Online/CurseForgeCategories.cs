using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.Core.Online;

/// <summary>The Sims 4 "Mods" class id (if CurseForge's category tree has exactly one class for this
/// game) plus its non-class categories, for the Browse tab's category filter.</summary>
public sealed record CurseForgeCategoryData(int? ModsClassId, IReadOnlyList<CurseForgeCategory> Categories);

/// <summary>
/// Caches CurseForge's category list for Sims 4, since it rarely changes and the Browse tab would
/// otherwise re-fetch it on every visit. Mirrors the file-based caching CurseForgeUpdateChecker
/// already does for fingerprints.
/// </summary>
public static class CurseForgeCategoryCache
{
    private static readonly string CacheFile = Path.Combine(AppPaths.Cache, "curseforge-categories.json");
    private const int MaxAgeDays = 7;

    public static async Task<CurseForgeCategoryData> LoadAsync(CurseForgeClient client, bool forceRefresh = false, CancellationToken cancel = default)
    {
        if (!forceRefresh && File.Exists(CacheFile)
            && (DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile)).TotalDays < MaxAgeDays
            && JsonFile.TryRead<CurseForgeCategoryData>(CacheFile) is { } cached)
            return cached;

        var classes = await client.GetCategoriesAsync(classId: null, classesOnly: true, cancel);
        // Expected: exactly one class for this game. If not, fall back to an unfiltered classId in
        // search (searches across every class) rather than guessing which one is "Mods".
        int? modsClassId = classes.Count == 1 ? classes[0].Id : null;

        var categories = await client.GetCategoriesAsync(modsClassId, classesOnly: false, cancel);
        var data = new CurseForgeCategoryData(
            modsClassId,
            categories.Where(c => !c.IsClass).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());

        try { JsonFile.WriteAtomic(CacheFile, data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* only an accelerator */ }
        return data;
    }
}
