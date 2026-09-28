using System.IO.Compression;
using System.Security.Cryptography;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Dbpf;

/// <summary>
/// Minimal reader for the DBPF (Database Packed File) format used by The Sims 4 .package files.
/// Scanning only parses the header and resource index table - payloads are read solely on demand
/// (<see cref="TryHashResources"/>) for resources that actually collide, which keeps scanning fast
/// even for large packages.
/// </summary>
public static class DbpfReader
{
    private const uint Magic = 0x46504244; // "DBPF" read as little-endian uint32
    private const int HeaderSize = 96;

    /// <summary>Index entry size when no field is shared via the index flags.</summary>
    private const int MinIndexEntrySize = 16; // at least instanceLo + offset + fileSize + memSize

    public const ushort CompressionNone = 0x0000;
    public const ushort CompressionZlib = 0x5A42;
    public const ushort CompressionDeleted = 0xFFE0;

    /// <summary>High bit of the stored size marks the extended (compression-type) entry layout.</summary>
    private const uint ExtendedEntryFlag = 0x80000000;

    /// <summary>
    /// Attempts to read the live resource index of a .package file. Entries marked as deleted
    /// are skipped - the game ignores them, so they cannot cause conflicts.
    /// Returns null if the file is not a recognizable DBPF package (corrupt, truncated,
    /// or an unsupported format version) so callers can exclude it from conflict detection
    /// instead of treating an empty result as "no resources".
    /// </summary>
    public static IReadOnlyList<PackageResource>? TryReadIndex(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            if (stream.Length < HeaderSize)
                return null;

            uint magic = reader.ReadUInt32();
            if (magic != Magic)
                return null;

            uint majorVersion = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // minorVersion
            if (majorVersion != 2)
                return null; // Sims 4 packages are DBPF v2.x; anything else is unsupported here.

            stream.Seek(0x24, SeekOrigin.Begin);
            uint indexEntryCount = reader.ReadUInt32();
            uint indexRecordPositionLow = reader.ReadUInt32();

            if (indexEntryCount == 0)
                return Array.Empty<PackageResource>();

            stream.Seek(0x40, SeekOrigin.Begin);
            long indexRecordPosition64 = reader.ReadInt64();

            long indexPosition = indexRecordPositionLow != 0 ? indexRecordPositionLow : indexRecordPosition64;
            if (indexPosition <= 0 || indexPosition >= stream.Length)
                return null;

            // A corrupt entry count would otherwise make us loop (and allocate) far past the file end.
            if ((long)indexEntryCount * MinIndexEntrySize > stream.Length - indexPosition)
                return null;

            stream.Seek(indexPosition, SeekOrigin.Begin);
            uint flags = reader.ReadUInt32();

            bool constType = (flags & 0x1) != 0;
            bool constGroup = (flags & 0x2) != 0;
            bool constInstanceHi = (flags & 0x4) != 0;

            uint sharedType = constType ? reader.ReadUInt32() : 0;
            uint sharedGroup = constGroup ? reader.ReadUInt32() : 0;
            uint sharedInstanceHi = constInstanceHi ? reader.ReadUInt32() : 0;

            var resources = new List<PackageResource>((int)indexEntryCount);

            for (uint i = 0; i < indexEntryCount; i++)
            {
                uint type = constType ? sharedType : reader.ReadUInt32();
                uint group = constGroup ? sharedGroup : reader.ReadUInt32();
                uint instanceHi = constInstanceHi ? sharedInstanceHi : reader.ReadUInt32();
                uint instanceLo = reader.ReadUInt32();

                uint chunkOffset = reader.ReadUInt32();
                uint rawFileSize = reader.ReadUInt32();
                uint memorySize = reader.ReadUInt32();

                ushort compressionType = CompressionNone;
                if ((rawFileSize & ExtendedEntryFlag) != 0)
                {
                    compressionType = reader.ReadUInt16();
                    _ = reader.ReadUInt16(); // committed flag
                }

                if (compressionType == CompressionDeleted)
                    continue;

                ulong instance = ((ulong)instanceHi << 32) | instanceLo;
                resources.Add(new PackageResource(
                    new ResourceKey(type, group, instance),
                    chunkOffset,
                    rawFileSize & ~ExtendedEntryFlag,
                    memorySize,
                    compressionType));
            }

            return resources;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Computes a content fingerprint for each requested resource of one package, opening the file once.
    /// <para>
    /// With <paramref name="decompress"/> false (fast), the payload is hashed as stored, tagged with its
    /// compression type and uncompressed size: equal fingerprints mean identical content, but the same
    /// content compressed differently yields different fingerprints.
    /// </para>
    /// <para>
    /// With <paramref name="decompress"/> true (slow), uncompressed and zlib payloads are hashed in
    /// decompressed form, so the same content compressed differently by two tools still matches. Other
    /// compression formats (e.g. RefPack) are still hashed as stored.
    /// </para>
    /// Resources that cannot be read map to null; if the file cannot be opened the result is empty.
    /// </summary>
    public static IReadOnlyDictionary<PackageResource, string?> TryHashResources(
        string filePath, IEnumerable<PackageResource> resources, bool decompress)
    {
        var result = new Dictionary<PackageResource, string?>();
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1, FileOptions.RandomAccess); // we always read whole chunks, so skip FileStream's buffer

            // Reading in file order keeps disk access sequential.
            foreach (var resource in resources.Distinct().OrderBy(r => r.ChunkOffset))
                result[resource] = TryHashResource(stream, resource, decompress);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>Reads and decompresses one resource; throws <see cref="InvalidDataException"/> on malformed data.</summary>
    public static byte[] ReadResource(Stream stream, PackageResource resource)
    {
        if (resource.ChunkOffset < 0 || resource.ChunkOffset + resource.StoredSize > stream.Length)
            throw new InvalidDataException("Resource lies outside the file.");

        var stored = new byte[resource.StoredSize];
        stream.Seek(resource.ChunkOffset, SeekOrigin.Begin);
        stream.ReadExactly(stored);
        return resource.CompressionType switch
        {
            RefPack.CompressionType => RefPack.Decompress(stored),
            CompressionZlib => Inflate(stored),
            _ => stored
        };
    }

    /// <summary>Reads several resources of one package (opening it once); unreadable ones are left out.</summary>
    public static IReadOnlyDictionary<PackageResource, byte[]> TryReadResources(string filePath, IEnumerable<PackageResource> resources)
    {
        var result = new Dictionary<PackageResource, byte[]>();
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            foreach (var resource in resources.Distinct().OrderBy(r => r.ChunkOffset))
            {
                try { result[resource] = ReadResource(stream, resource); }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { /* damaged resource: skip */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* locked or vanished: nothing readable */ }
        return result;
    }

    private static byte[] Inflate(byte[] stored)
    {
        using var zlib = new ZLibStream(new MemoryStream(stored), CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static string? TryHashResource(FileStream stream, PackageResource resource, bool decompress)
    {
        if (resource.ChunkOffset < 0 || resource.ChunkOffset + resource.StoredSize > stream.Length)
            return null;

        try
        {
            var stored = new byte[resource.StoredSize];
            stream.Seek(resource.ChunkOffset, SeekOrigin.Begin);
            stream.ReadExactly(stored);

            if (!decompress || resource.CompressionType is not (CompressionNone or CompressionZlib))
            {
                return $"{resource.CompressionType:X4}:{resource.MemorySize}:" +
                       Convert.ToHexString(SHA256.HashData(stored));
            }

            if (resource.CompressionType == CompressionNone)
                return "raw:" + Convert.ToHexString(SHA256.HashData(stored));

            using var zlib = new ZLibStream(new MemoryStream(stored), CompressionMode.Decompress);
            return "raw:" + Convert.ToHexString(SHA256.HashData(zlib));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }
}
