using System.Text;

namespace Sims4ModManager.Core.Tests;

/// <summary>Writes tray files in the game's format (8-byte header + protobuf) for tests.</summary>
internal static class TrayTestFiles
{
    /// <summary>Tiny protobuf writer - just enough for TrayMetadata.</summary>
    internal sealed class Proto
    {
        private readonly MemoryStream _stream = new();

        public Proto Varint(int field, ulong value)
        {
            WriteVarint((ulong)(field << 3));
            WriteVarint(value);
            return this;
        }

        public Proto Bytes(int field, byte[] value)
        {
            WriteVarint((ulong)(field << 3) | 2);
            WriteVarint((ulong)value.Length);
            _stream.Write(value);
            return this;
        }

        public Proto String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
        public Proto Message(int field, Proto message) => Bytes(field, message.ToArray());

        public byte[] ToArray() => _stream.ToArray();

        private void WriteVarint(ulong value)
        {
            while (value >= 0x80)
            {
                _stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            _stream.WriteByte((byte)value);
        }
    }

    public static string FileName(uint group, ulong instance, string extension) =>
        $"0x{group:x8}!0x{instance:x16}.{extension}";

    public static byte[] TrayItem(ulong id, int type, string name, string creator = "Tester",
        IEnumerable<(string First, string Last, ulong Id)>? sims = null, (int W, int D)? lotSize = null, string? tags = null)
    {
        var specific = new Proto();
        if (sims is not null)
        {
            var household = new Proto().Varint(1, (ulong)sims.Count());
            foreach (var (first, last, simId) in sims)
                household.Message(2, new Proto().String(3, first).String(4, last).Varint(5, simId));
            specific.Message(2, household);
        }
        if (lotSize is not null)
            specific.Message(1, new Proto().Varint(2, (ulong)lotSize.Value.W).Varint(3, (ulong)lotSize.Value.D));
        if (tags is not null)
            specific.String(8, tags);

        var body = new Proto()
            .Varint(1, id)
            .Varint(2, (ulong)type)
            .String(4, name)
            .String(5, "Beschreibung")
            .String(7, creator)
            .Message(10, specific)
            .Varint(11, 63_916_798_869) // 2026
            .ToArray();

        var file = new byte[8 + body.Length];
        BitConverter.GetBytes(body.Length).CopyTo(file, 4);
        body.CopyTo(file, 8);
        return file;
    }

    /// <summary>A data file (e.g. .householdbinary) that references the given instances as protobuf varints.</summary>
    public static byte[] DataReferencing(params ulong[] instances)
    {
        var proto = new Proto().String(1, "padding");
        foreach (var instance in instances)
            proto.Varint(7, instance);
        return new byte[] { 2, 0, 0, 0 }.Concat(proto.ToArray()).ToArray();
    }
}
