namespace Sims4ModManager.Core.Models;

/// <summary>
/// One entry of a DBPF package's resource index: the key plus where (and how) its payload is
/// stored. Location data is kept so conflicting resources can later be compared by content
/// without re-parsing the index.
/// </summary>
public readonly record struct PackageResource(
    ResourceKey Key,
    long ChunkOffset,
    uint StoredSize,
    uint MemorySize,
    ushort CompressionType);
