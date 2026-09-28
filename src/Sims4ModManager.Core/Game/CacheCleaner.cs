using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Game;

public sealed record CacheStatus(long SizeBytes, int FileCount, DateTime? LastWriteUtc, bool IsStale);

/// <summary>
/// The game's caches - deleting them after changing mods is the standard community fix for CC that
/// does not show up or old thumbnails. The game rebuilds them on the next start.
/// </summary>
public static class CacheCleaner
{
    private static readonly string[] CacheFiles = { "localthumbcache.package", "avatarcache.package" };
    private static readonly string[] CacheFolders = { "cachestr", "onlinethumbnailcache" };

    public static IEnumerable<string> EnumerateCacheFiles(string gameDataFolder)
    {
        foreach (string name in CacheFiles)
        {
            string path = Path.Combine(gameDataFolder, name);
            if (File.Exists(path))
                yield return path;
        }
        foreach (string name in CacheFolders)
        {
            string dir = Path.Combine(gameDataFolder, name);
            if (!Directory.Exists(dir))
                continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                yield return file;
        }
    }

    /// <summary>
    /// Size and age of the caches. "Stale" means a mod file changed after the cache was last
    /// written, so thumbnails/CAS lists may be outdated until the cache is cleared.
    /// </summary>
    public static CacheStatus GetStatus(string gameDataFolder, IEnumerable<ModEntry> mods)
    {
        var files = EnumerateCacheFiles(gameDataFolder).Select(f => new FileInfo(f)).ToList();
        DateTime? lastWrite = files.Count == 0 ? null : files.Max(f => f.LastWriteTimeUtc);
        DateTime newestMod = mods.Select(m => m.LastModifiedUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        bool stale = lastWrite is not null && newestMod > lastWrite.Value;
        return new CacheStatus(files.Sum(f => f.Length), files.Count, lastWrite, stale);
    }

    /// <summary>Removes the cache files through the journal (so even this can be undone).</summary>
    public static int Clear(string gameDataFolder, ChangeRecorder recorder)
    {
        int count = 0;
        foreach (string file in EnumerateCacheFiles(gameDataFolder).ToList())
        {
            recorder.Delete(file);
            count++;
        }
        return count;
    }
}
