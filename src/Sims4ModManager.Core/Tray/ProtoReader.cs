namespace Sims4ModManager.Core.Tray;

/// <summary>
/// Minimal protobuf wire-format reader. Tray metadata is protobuf, but only a handful of fields
/// are needed, so this walks the raw wire format instead of pulling in generated message classes.
/// Unknown fields are skipped; malformed data ends the iteration instead of throwing.
/// </summary>
internal static class ProtoReader
{
    public enum WireType { Varint = 0, Fixed64 = 1, LengthDelimited = 2, Fixed32 = 5 }

    public readonly record struct Field(int Number, WireType Type, ulong Value, int Offset, int Length);

    /// <summary>Enumerates the top-level fields of a message. For length-delimited fields, Offset/Length locate the payload.</summary>
    public static IEnumerable<Field> ReadFields(byte[] data, int offset, int length)
    {
        int pos = offset;
        int end = offset + length;
        while (pos < end)
        {
            if (!TryReadVarint(data, ref pos, end, out ulong tag))
                yield break;

            int number = (int)(tag >> 3);
            var type = (WireType)(tag & 0x7);
            if (number <= 0)
                yield break;

            switch (type)
            {
                case WireType.Varint:
                    if (!TryReadVarint(data, ref pos, end, out ulong value))
                        yield break;
                    yield return new Field(number, type, value, 0, 0);
                    break;

                case WireType.Fixed64:
                    if (end - pos < 8)
                        yield break;
                    yield return new Field(number, type, BitConverter.ToUInt64(data, pos), 0, 0);
                    pos += 8;
                    break;

                case WireType.Fixed32:
                    if (end - pos < 4)
                        yield break;
                    yield return new Field(number, type, BitConverter.ToUInt32(data, pos), 0, 0);
                    pos += 4;
                    break;

                case WireType.LengthDelimited:
                    if (!TryReadVarint(data, ref pos, end, out ulong len) || len > (ulong)(end - pos))
                        yield break;
                    yield return new Field(number, type, 0, pos, (int)len);
                    pos += (int)len;
                    break;

                default:
                    yield break; // groups (3/4) are not used by the game's messages
            }
        }
    }

    public static bool TryReadVarint(byte[] data, ref int pos, int end, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64 && pos < end; shift += 7)
        {
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
        }
        return false;
    }

    public static string ReadString(byte[] data, Field field) =>
        System.Text.Encoding.UTF8.GetString(data, field.Offset, field.Length);
}
