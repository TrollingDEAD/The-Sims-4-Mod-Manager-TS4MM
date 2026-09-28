using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Dbpf;

/// <summary>
/// One resource of a package to write: either copied byte for byte from an existing package
/// (compression untouched) or given as stored bytes.
/// </summary>
public readonly record struct PackageWriteEntry(
    ResourceKey Key, uint MemorySize, ushort CompressionType, uint StoredSize,
    string? SourcePath, long SourceOffset, byte[]? Stored)
{
    public static PackageWriteEntry Copy(string sourcePath, PackageResource resource) =>
        new(resource.Key, resource.MemorySize, resource.CompressionType, resource.StoredSize, sourcePath, resource.ChunkOffset, null);

    public static PackageWriteEntry FromBytes(ResourceKey key, byte[] stored, uint memorySize, ushort compressionType) =>
        new(key, memorySize, compressionType, (uint)stored.Length, null, 0, stored);
}

/// <summary>
/// Writes DBPF v2 packages. The index is written in the plain layout every Sims 4 tool reads (no
/// shared index fields, extended entries with compression type).
/// </summary>
public static class DbpfWriter
{
    private const int HeaderSize = 96;
    private const uint ExtendedEntryFlag = 0x80000000;

    /// <summary>The game's package format limit: offsets in the index are 32 bit.</summary>
    public const long MaxPackageBytes = uint.MaxValue;

    /// <summary>
    /// Writes a copy of <paramref name="sourcePath"/> without the resources whose keys are in
    /// <paramref name="keysToRemove"/> to <paramref name="destinationPath"/>. Returns how many index
    /// entries were removed. Throws <see cref="InvalidDataException"/> if the source is not a readable package.
    /// </summary>
    public static int WriteWithout(string sourcePath, IReadOnlySet<ResourceKey> keysToRemove, string destinationPath)
    {
        var resources = ReadIndexOrThrow(sourcePath);
        var kept = resources.Where(r => !keysToRemove.Contains(r.Key)).ToList();

        // Payloads in original file order for sequential reads; index order stays as it was.
        Write(destinationPath, kept.OrderBy(r => r.ChunkOffset).Select(r => PackageWriteEntry.Copy(sourcePath, r)), ReadHeader(sourcePath));
        return resources.Count - kept.Count;
    }

    /// <summary>
    /// Writes a package from <paramref name="entries"/> (payloads in the given order). The header is
    /// taken from <paramref name="headerTemplate"/> (to keep an existing package's header) or created new.
    /// </summary>
    public static void Write(string destinationPath, IEnumerable<PackageWriteEntry> entries, byte[]? headerTemplate = null)
    {
        var list = entries.ToList();
        var sources = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            using var writer = new BinaryWriter(output);

            output.Write(headerTemplate is { Length: HeaderSize } ? headerTemplate : NewHeader()); // patched below

            var offsets = new long[list.Count];
            var buffer = new byte[81920];
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                offsets[i] = output.Position;
                if (entry.Stored is not null)
                {
                    output.Write(entry.Stored);
                    continue;
                }
                if (!sources.TryGetValue(entry.SourcePath!, out var input))
                    sources[entry.SourcePath!] = input = new FileStream(entry.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                input.Seek(entry.SourceOffset, SeekOrigin.Begin);
                CopyExactly(input, output, entry.StoredSize, buffer);
            }

            if (output.Position > MaxPackageBytes)
                throw new InvalidDataException(Localization.L.T("Das Package würde größer als 4 GB – so große Packages kann das Spiel nicht lesen."));

            // Index: flags = 0 (every field stored per entry).
            long indexPosition = output.Position;
            writer.Write(0u);
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                writer.Write(e.Key.Type);
                writer.Write(e.Key.Group);
                writer.Write((uint)(e.Key.Instance >> 32));
                writer.Write((uint)e.Key.Instance);
                writer.Write((uint)offsets[i]);
                writer.Write(e.StoredSize | ExtendedEntryFlag);
                writer.Write(e.MemorySize);
                writer.Write(e.CompressionType);
                writer.Write((ushort)1); // committed
            }
            long indexSize = output.Position - indexPosition;

            // Header: entry count, index position/size; hole index cleared (we write no holes).
            output.Seek(0x24, SeekOrigin.Begin);
            writer.Write((uint)list.Count);        // 0x24 index entry count
            writer.Write(0u);                      // 0x28 index position (low, legacy) - 0 = use 64-bit field
            writer.Write((uint)indexSize);         // 0x2C index size
            writer.Write(0u);                      // 0x30 hole entry count
            writer.Write(0u);                      // 0x34 hole index position
            writer.Write(0u);                      // 0x38 hole index size
            output.Seek(0x40, SeekOrigin.Begin);
            writer.Write(indexPosition);           // 0x40 index position (64-bit)
            writer.Flush();
            output.Flush(flushToDisk: true);
        }
        finally
        {
            foreach (var stream in sources.Values)
                stream.Dispose();
        }
    }

    internal static IReadOnlyList<PackageResource> ReadIndexOrThrow(string path) =>
        DbpfReader.TryReadIndex(path)
        ?? throw new InvalidDataException(Localization.L.F("{0} ist kein lesbares Package.", Path.GetFileName(path)));

    private static byte[] ReadHeader(string path)
    {
        var header = new byte[HeaderSize];
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        input.ReadExactly(header);
        return header;
    }

    /// <summary>DBPF 2.1 header with index version 3, as the game and Sims 4 Studio write it.</summary>
    private static byte[] NewHeader()
    {
        var header = new byte[HeaderSize];
        "DBPF"u8.CopyTo(header);
        BitConverter.GetBytes(2u).CopyTo(header, 0x04); // major
        BitConverter.GetBytes(1u).CopyTo(header, 0x08); // minor
        BitConverter.GetBytes(3u).CopyTo(header, 0x3C); // index minor version
        return header;
    }

    private static void CopyExactly(Stream input, Stream output, long count, byte[] buffer)
    {
        while (count > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
                throw new EndOfStreamException(Localization.L.T("Package ist kürzer als sein Index angibt."));
            output.Write(buffer, 0, read);
            count -= read;
        }
    }
}
