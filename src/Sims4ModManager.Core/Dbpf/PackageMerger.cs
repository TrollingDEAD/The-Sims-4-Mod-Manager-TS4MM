using System.Text;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Dbpf;

/// <summary>One original file recorded in a merged package: its name (without extension) and its resources.</summary>
public sealed record MergeManifestEntry(string Name, IReadOnlyList<ResourceKey> Keys);

public sealed record MergeResult(int FileCount, int ResourceCount, int DuplicatesSkipped, long Bytes);

/// <summary>A file that <see cref="PackageMerger.Unmerge"/> would restore.</summary>
public sealed record UnmergePart(string FileName, IReadOnlyList<ResourceKey> Keys);

/// <summary>
/// Merging many small CC files into one package and splitting merged packages again. The manifest
/// (type 0x7FB6AD8A) is written in the format of Sims 4 Studio, so packages merged here can be
/// unmerged there and vice versa: uint32 version (1), 8 zero bytes, uint32 file count, then per file a
/// UTF-8 name (uint32 byte length, no extension), uint32 key count and the keys (instance, type, group).
/// </summary>
public static class PackageMerger
{
    public const uint ManifestType = 0x7FB6AD8A;
    private static readonly ResourceKey ManifestKey = new(ManifestType, 0, 0);

    /// <summary>Community advice: merged packages should stay well below the format's limit.</summary>
    public const long RecommendedMaxBytes = 1L << 30;
    public const int RecommendedMaxFiles = 500;

    public static bool IsMerged(IReadOnlyList<PackageResource> resources) => resources.Any(r => r.Key.Type == ManifestType);

    /// <summary>The manifest of a merged package, or null if it has none (or it cannot be read).</summary>
    public static IReadOnlyList<MergeManifestEntry>? TryReadManifest(string path)
    {
        var resources = DbpfReader.TryReadIndex(path);
        var manifest = resources?.Where(r => r.Key.Type == ManifestType).Cast<PackageResource?>().FirstOrDefault();
        if (manifest is null)
            return null;
        return DbpfReader.TryReadResources(path, new[] { manifest.Value }).TryGetValue(manifest.Value, out var data)
            ? TryParseManifest(data)
            : null;
    }

    public static IReadOnlyList<MergeManifestEntry>? TryParseManifest(byte[] data)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            if (reader.ReadUInt32() != 1)
                return null;
            reader.ReadUInt64();
            uint count = reader.ReadUInt32();
            if (count > 100_000)
                return null;
            var entries = new List<MergeManifestEntry>((int)count);
            for (int i = 0; i < count; i++)
            {
                int nameLength = reader.ReadInt32();
                if (nameLength is < 0 or > 4096)
                    return null;
                string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                uint keyCount = reader.ReadUInt32();
                if (keyCount > (data.Length - reader.BaseStream.Position) / 16)
                    return null;
                var keys = new List<ResourceKey>((int)keyCount);
                for (int k = 0; k < keyCount; k++)
                {
                    ulong instance = reader.ReadUInt64();
                    uint type = reader.ReadUInt32();
                    uint group = reader.ReadUInt32();
                    keys.Add(new ResourceKey(type, group, instance));
                }
                entries.Add(new MergeManifestEntry(name, keys));
            }
            return entries;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    public static byte[] BuildManifest(IReadOnlyList<MergeManifestEntry> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1u);
        writer.Write(0UL);
        writer.Write((uint)entries.Count);
        foreach (var entry in entries)
        {
            byte[] name = Encoding.UTF8.GetBytes(entry.Name);
            writer.Write(name.Length);
            writer.Write(name);
            writer.Write((uint)entry.Keys.Count);
            foreach (var key in entry.Keys)
            {
                writer.Write(key.Instance);
                writer.Write(key.Type);
                writer.Write(key.Group);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Merges <paramref name="inputs"/> (in this order) into <paramref name="destination"/>. A resource
    /// that several inputs contain is taken from the first one. Inputs that are merged packages
    /// themselves keep their original file list, so unmerging restores the small files.
    /// </summary>
    public static MergeResult Merge(IReadOnlyList<string> inputs, string destination)
    {
        var seen = new HashSet<ResourceKey>();
        var entries = new List<PackageWriteEntry>();
        var manifest = new List<MergeManifestEntry>();
        int duplicates = 0;

        foreach (string input in inputs)
        {
            var resources = DbpfWriter.ReadIndexOrThrow(input);
            var taken = new List<PackageResource>();
            foreach (var resource in resources.OrderBy(r => r.ChunkOffset))
            {
                if (resource.Key.Type == ManifestType)
                    continue;
                if (!seen.Add(resource.Key))
                {
                    duplicates++;
                    continue;
                }
                taken.Add(resource);
                entries.Add(PackageWriteEntry.Copy(input, resource));
            }

            var takenKeys = taken.Select(r => r.Key).ToHashSet();
            var nested = IsMerged(resources) ? TryReadManifest(input) : null;
            if (nested is not null)
            {
                foreach (var part in nested)
                {
                    var keys = part.Keys.Where(takenKeys.Remove).ToList();
                    if (keys.Count > 0)
                        manifest.Add(part with { Keys = keys });
                }
            }
            if (takenKeys.Count > 0)
                manifest.Add(new MergeManifestEntry(Path.GetFileNameWithoutExtension(input),
                    taken.Select(r => r.Key).Where(takenKeys.Contains).ToList()));
        }

        byte[] manifestBytes = BuildManifest(manifest);
        entries.Add(PackageWriteEntry.FromBytes(ManifestKey, manifestBytes, (uint)manifestBytes.Length, DbpfReader.CompressionNone));
        DbpfWriter.Write(destination, entries);
        return new MergeResult(inputs.Count, entries.Count - 1, duplicates, new FileInfo(destination).Length);
    }

    /// <summary>
    /// The files unmerging would restore, with file names made safe and unique. Resources the manifest
    /// does not list end up in "&lt;name&gt;_Rest.package". Null if the package is not merged.
    /// </summary>
    public static IReadOnlyList<UnmergePart>? PlanUnmerge(string mergedPath)
    {
        var resources = DbpfReader.TryReadIndex(mergedPath);
        var manifest = TryReadManifest(mergedPath);
        if (resources is null || manifest is null)
            return null;

        var present = resources.Select(r => r.Key).Where(k => k.Type != ManifestType).ToHashSet();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<UnmergePart>();
        foreach (var entry in manifest)
        {
            var keys = entry.Keys.Where(present.Remove).ToList();
            if (keys.Count > 0)
                parts.Add(new UnmergePart(UniqueName(entry.Name, used), keys));
        }
        if (present.Count > 0)
            parts.Add(new UnmergePart(UniqueName(Path.GetFileNameWithoutExtension(mergedPath) + "_Rest", used), present.ToList()));
        return parts;
    }

    /// <summary>Writes one part of a merged package (see <see cref="PlanUnmerge"/>) to <paramref name="destination"/>.</summary>
    public static void WritePart(string mergedPath, UnmergePart part, string destination)
    {
        var wanted = part.Keys.ToHashSet();
        var resources = DbpfWriter.ReadIndexOrThrow(mergedPath)
            .Where(r => wanted.Remove(r.Key)) // first occurrence only
            .OrderBy(r => r.ChunkOffset);
        DbpfWriter.Write(destination, resources.Select(r => PackageWriteEntry.Copy(mergedPath, r)));
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (safe.Length == 0)
            safe = "Teil";
        string candidate = safe + ".package";
        for (int i = 2; !used.Add(candidate); i++)
            candidate = $"{safe} ({i}).package";
        return candidate;
    }
}
