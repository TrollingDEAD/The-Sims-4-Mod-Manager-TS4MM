namespace Sims4ModManager.Core.Dbpf;

/// <summary>
/// Decompressor for RefPack (a.k.a. QFS), EA's LZ77 variant used for package resources with
/// compression type 0xFFFF - e.g. almost everything inside save files.
/// </summary>
public static class RefPack
{
    public const ushort CompressionType = 0xFFFF;

    /// <summary>Decompresses RefPack data; throws <see cref="InvalidDataException"/> on malformed input.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5 || data[1] != 0xFB)
            throw new InvalidDataException("Keine RefPack-Daten.");

        byte flags = data[0];
        bool largeSizes = (flags & 0x80) != 0;
        int sizeBytes = largeSizes ? 4 : 3;
        int pos = 2;
        if ((flags & 0x01) != 0)
            pos += sizeBytes; // compressed size (unused)

        int outputSize = 0;
        for (int i = 0; i < sizeBytes; i++)
            outputSize = (outputSize << 8) | data[pos++];

        var output = new byte[outputSize];
        int outPos = 0;

        while (pos < data.Length)
        {
            byte b0 = data[pos++];
            int plain, copy = 0, offset = 0;

            if (b0 < 0x80)
            {
                byte b1 = data[pos++];
                plain = b0 & 0x03;
                copy = ((b0 & 0x1C) >> 2) + 3;
                offset = ((b0 & 0x60) << 3) + b1 + 1;
            }
            else if (b0 < 0xC0)
            {
                byte b1 = data[pos++], b2 = data[pos++];
                plain = (b1 >> 6) & 0x03;
                copy = (b0 & 0x3F) + 4;
                offset = ((b1 & 0x3F) << 8) + b2 + 1;
            }
            else if (b0 < 0xE0)
            {
                byte b1 = data[pos++], b2 = data[pos++], b3 = data[pos++];
                plain = b0 & 0x03;
                copy = ((b0 & 0x0C) << 6) + b3 + 5;
                offset = ((b0 & 0x10) << 12) + (b1 << 8) + b2 + 1;
            }
            else if (b0 < 0xFC)
            {
                plain = ((b0 & 0x1F) << 2) + 4;
            }
            else
            {
                plain = b0 & 0x03; // end of stream after these literals
                Literal(data, ref pos, output, ref outPos, plain);
                break;
            }

            Literal(data, ref pos, output, ref outPos, plain);

            if (copy > 0)
            {
                int from = outPos - offset;
                if (from < 0 || outPos + copy > output.Length)
                    throw new InvalidDataException("Ungültige RefPack-Rückreferenz.");
                for (int i = 0; i < copy; i++) // byte by byte: source and target may overlap
                    output[outPos++] = output[from + i];
            }
        }

        if (outPos != output.Length)
            throw new InvalidDataException("RefPack-Daten unvollständig.");
        return output;
    }

    private static void Literal(ReadOnlySpan<byte> data, ref int pos, byte[] output, ref int outPos, int count)
    {
        if (count == 0)
            return;
        if (pos + count > data.Length || outPos + count > output.Length)
            throw new InvalidDataException("RefPack-Daten abgeschnitten.");
        data.Slice(pos, count).CopyTo(output.AsSpan(outPos));
        pos += count;
        outPos += count;
    }
}
