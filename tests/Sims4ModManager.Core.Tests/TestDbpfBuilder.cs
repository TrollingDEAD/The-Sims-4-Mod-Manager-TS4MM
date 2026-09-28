using System.IO.Compression;

namespace Sims4ModManager.Core.Tests;

/// <summary>Builds minimal, spec-shaped DBPF v2 byte streams for testing <see cref="Dbpf.DbpfReader"/>.</summary>
internal static class TestDbpfBuilder
{
    public const ushort Uncompressed = 0x0000;
    public const ushort Zlib = 0x5A42;
    public const ushort Deleted = 0xFFE0;

    public sealed record Entry(uint Type, uint Group, ulong Instance, byte[]? Data = null, ushort Compression = Uncompressed);

    public static byte[] Build(IReadOnlyList<(uint Type, uint Group, ulong Instance)> entries, uint indexFlags = 0) =>
        Build(entries.Select(e => new Entry(e.Type, e.Group, e.Instance)).ToList(), indexFlags);

    public static byte[] Build(IReadOnlyList<Entry> entries, uint indexFlags = 0)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // --- Header (96 bytes) ---
        w.Write((byte)'D'); w.Write((byte)'B'); w.Write((byte)'P'); w.Write((byte)'F');
        w.Write((uint)2);   // majorVersion
        w.Write((uint)1);   // minorVersion
        w.Write((uint)0);   // unknown1
        w.Write((uint)0);   // unknown2
        w.Write((uint)0);   // flags
        w.Write((uint)0);   // dateCreated
        w.Write((uint)0);   // dateModified
        w.Write((uint)3);   // indexMajorVersion
        w.Write((uint)entries.Count); // indexEntryCount
        w.Write((uint)0);   // indexRecordPositionLow (0 -> reader falls back to 64-bit field)
        w.Write((uint)0);   // indexSize
        w.Write((uint)0);   // numHoleEntries
        w.Write((uint)0);   // holeIndexSize
        w.Write((uint)0);   // holeIndexPosition
        w.Write((uint)0);   // indexMinorVersion
        long indexPositionField = ms.Position;
        w.Write((long)0);   // indexRecordPosition (patched below)
        w.Write(new byte[24]); // unused padding to reach 96 bytes total

        // --- Payloads (directly after the header) ---
        var stored = new List<(long Offset, int StoredSize, int MemSize)>();
        foreach (var e in entries)
        {
            byte[] data = e.Data ?? Array.Empty<byte>();
            byte[] bytes = e.Compression == Zlib ? ZlibCompress(data) : data;
            stored.Add((ms.Position, bytes.Length, data.Length));
            w.Write(bytes);
        }

        // --- Index ---
        long indexPosition = ms.Position;
        bool constType = (indexFlags & 0x1) != 0;
        bool constGroup = (indexFlags & 0x2) != 0;
        bool constInstanceHi = (indexFlags & 0x4) != 0;

        w.Write(indexFlags);
        if (constType) w.Write(entries[0].Type);
        if (constGroup) w.Write(entries[0].Group);
        if (constInstanceHi) w.Write((uint)(entries[0].Instance >> 32));

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!constType) w.Write(e.Type);
            if (!constGroup) w.Write(e.Group);
            if (!constInstanceHi) w.Write((uint)(e.Instance >> 32));
            w.Write((uint)e.Instance); // instanceLo
            w.Write((uint)stored[i].Offset); // chunkOffset
            w.Write((uint)stored[i].StoredSize | 0x80000000u); // fileSize + "extended entry" flag
            w.Write((uint)stored[i].MemSize); // memSize
            w.Write(e.Compression); // compression type
            w.Write((ushort)1); // committed
        }

        ms.Position = indexPositionField;
        w.Write(indexPosition);

        return ms.ToArray();
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
            zlib.Write(data);
        return output.ToArray();
    }
}
