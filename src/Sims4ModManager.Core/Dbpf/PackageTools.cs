using System.IO.Compression;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Dbpf;

/// <summary>What <see cref="PackageTools.Inspect"/> found in a package.</summary>
public sealed record PackageInspection(
    int ResourceCount,
    int DuplicateEntries,
    int UncompressedCount,
    long UncompressedBytes,
    bool IsEmpty)
{
    public bool CanOptimize => DuplicateEntries > 0 || UncompressedCount > 0;
}

public sealed record OptimizeResult(int DuplicatesRemoved, int Compressed, long BytesBefore, long BytesAfter)
{
    public long Saved => BytesBefore - BytesAfter;
}

/// <summary>
/// Small repairs Sims 4 Studio offers as batch fixes: removing duplicate index entries, compressing
/// uncompressed resources, and recognizing packages without content.
/// </summary>
public static class PackageTools
{
    /// <summary>Uncompressed resources smaller than this gain nothing from compression.</summary>
    private const uint MinCompressSize = 256;

    public static PackageInspection Inspect(IReadOnlyList<PackageResource> resources)
    {
        int duplicates = resources.Count - resources.Select(r => r.Key).Distinct().Count();
        var uncompressed = resources.Where(IsCompressible).ToList();
        return new PackageInspection(resources.Count, duplicates, uncompressed.Count,
            uncompressed.Sum(r => (long)r.StoredSize), IsEmpty(resources));
    }

    /// <summary>No resources at all, or only a merge manifest: the file does nothing in the game.</summary>
    public static bool IsEmpty(IReadOnlyList<PackageResource> resources) =>
        resources.All(r => r.Key.Type == PackageMerger.ManifestType);

    /// <summary>The merge manifest stays uncompressed, as Sims 4 Studio writes it.</summary>
    private static bool IsCompressible(PackageResource r) =>
        r.CompressionType == DbpfReader.CompressionNone && r.MemorySize >= MinCompressSize && r.Key.Type != PackageMerger.ManifestType;

    /// <summary>
    /// Writes an optimized copy of <paramref name="sourcePath"/>: of several index entries with the
    /// same key only the last is kept (the one tools read last), uncompressed resources are
    /// zlib-compressed where that makes them smaller. The content the game sees stays the same.
    /// </summary>
    public static OptimizeResult Optimize(string sourcePath, string destinationPath, bool removeDuplicates = true, bool compress = true)
    {
        var resources = DbpfWriter.ReadIndexOrThrow(sourcePath);
        var kept = removeDuplicates
            ? resources.Select((r, i) => (r, i)).GroupBy(x => x.r.Key).Select(g => g.Last()).OrderBy(x => x.i).Select(x => x.r).ToList()
            : resources.ToList();

        int compressed = 0;
        var entries = new List<PackageWriteEntry>(kept.Count);
        using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        {
            foreach (var resource in kept.OrderBy(r => r.ChunkOffset))
            {
                if (compress && IsCompressible(resource))
                {
                    byte[] raw = DbpfReader.ReadResource(input, resource);
                    byte[] packed = Deflate(raw);
                    if (packed.Length < raw.Length)
                    {
                        entries.Add(PackageWriteEntry.FromBytes(resource.Key, packed, (uint)raw.Length, DbpfReader.CompressionZlib));
                        compressed++;
                        continue;
                    }
                }
                entries.Add(PackageWriteEntry.Copy(sourcePath, resource));
            }
        }

        DbpfWriter.Write(destinationPath, entries);
        return new OptimizeResult(resources.Count - kept.Count, compressed, new FileInfo(sourcePath).Length, new FileInfo(destinationPath).Length);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
            zlib.Write(data);
        return output.ToArray();
    }
}
