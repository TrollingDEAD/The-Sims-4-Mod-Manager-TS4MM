using Sims4ModManager.Core.Localization;
using System.Security.Cryptography;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Catalog;

public sealed record StorageBucket(string Label, long Bytes, int Count);

/// <summary>Files with byte-identical content: all but one are wasted space.</summary>
public sealed record DuplicateSet(IReadOnlyList<(ModEntry Mod, ModFileInfo File)> Files)
{
    public long WastedBytes => Files.Count < 2 ? 0 : Files[0].File.SizeBytes * (Files.Count - 1);
}

public sealed record StorageReport(
    long TotalBytes,
    int FileCount,
    long DisabledBytes,
    long UncompressedBytes,
    IReadOnlyList<StorageBucket> ByCategory,
    IReadOnlyList<StorageBucket> ByCreator,
    IReadOnlyList<ModEntry> Largest,
    IReadOnlyList<DuplicateSet> Duplicates)
{
    public long WastedByDuplicates => Duplicates.Sum(d => d.WastedBytes);
}

/// <summary>Where the space in the Mods folder goes: categories, creators, largest mods, duplicates, uncompressed data.</summary>
public static class StorageAnalyzer
{
    /// <summary>Uncompressed resources smaller than this are not worth mentioning.</summary>
    private const uint UncompressedThreshold = 4096;

    public static StorageReport Analyze(
        IReadOnlyList<ModEntry> mods, Func<ModEntry, string> category, Func<ModEntry, string?> creator,
        CancellationToken cancel = default)
    {
        var files = mods.SelectMany(m => m.Files.Select(f => (Mod: m, File: f))).ToList();

        var byCategory = mods.GroupBy(category)
            .Select(g => new StorageBucket(g.Key, g.Sum(m => m.TotalSizeBytes), g.Count()))
            .OrderByDescending(b => b.Bytes).ToList();
        var byCreator = mods.GroupBy(m => creator(m) ?? L.T("(unbekannt)"))
            .Select(g => new StorageBucket(g.Key, g.Sum(m => m.TotalSizeBytes), g.Count()))
            .OrderByDescending(b => b.Bytes).ToList();

        long uncompressed = files.SelectMany(f => f.File.Resources)
            .Where(r => r.CompressionType == DbpfReader.CompressionNone && r.MemorySize >= UncompressedThreshold)
            .Sum(r => (long)r.StoredSize);

        return new StorageReport(
            files.Sum(f => f.File.SizeBytes),
            files.Count,
            files.Where(f => !f.File.IsEnabled).Sum(f => f.File.SizeBytes),
            uncompressed,
            byCategory,
            byCreator,
            mods.OrderByDescending(m => m.TotalSizeBytes).Take(25).ToList(),
            FindDuplicates(files, cancel));
    }

    /// <summary>Only files of equal size are hashed, so this stays fast even for large folders.</summary>
    private static List<DuplicateSet> FindDuplicates(List<(ModEntry Mod, ModFileInfo File)> files, CancellationToken cancel)
    {
        var result = new List<DuplicateSet>();
        foreach (var sameSize in files.Where(f => f.File.SizeBytes > 1024).GroupBy(f => f.File.SizeBytes).Where(g => g.Count() > 1))
        {
            cancel.ThrowIfCancellationRequested();
            var byHash = sameSize.Select(f => (f.Mod, f.File, Hash: TryHash(f.File.AbsolutePath)))
                .Where(x => x.Hash is not null)
                .GroupBy(x => x.Hash!)
                .Where(g => g.Count() > 1);
            foreach (var group in byHash)
                result.Add(new DuplicateSet(group.Select(x => (x.Mod, x.File)).ToList()));
        }
        return result.OrderByDescending(d => d.WastedBytes).ToList();
    }

    private static string? TryHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
