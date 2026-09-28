namespace Sims4ModManager.Core.Online;

/// <summary>
/// The file fingerprint CurseForge identifies files by: 32-bit MurmurHash2 (seed 1) over the file's
/// bytes with the whitespace bytes 0x09, 0x0A, 0x0D and 0x20 left out.
/// </summary>
public static class CurseForgeFingerprint
{
    private const uint M = 0x5BD1E995;
    private const int R = 24;
    private const uint Seed = 1;

    public static uint Compute(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return Compute(stream);
    }

    public static uint Compute(Stream stream)
    {
        // First pass: the hash is seeded with the length of the filtered data.
        long start = stream.CanSeek ? stream.Position : 0;
        var buffer = new byte[1 << 16];
        uint length = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++)
                if (!IsWhitespace(buffer[i]))
                    length++;

        stream.Seek(start, SeekOrigin.Begin);
        uint h = Seed ^ length;
        uint word = 0;
        int shift = 0;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                if (IsWhitespace(b))
                    continue;
                word |= (uint)b << shift;
                shift += 8;
                if (shift == 32)
                {
                    uint k = word * M;
                    k ^= k >> R;
                    k *= M;
                    h = (h * M) ^ k;
                    word = 0;
                    shift = 0;
                }
            }
        }

        if (shift > 0)
        {
            h ^= word; // the remaining 1-3 bytes, little-endian, exactly as the reference tail switch
            h *= M;
        }
        h ^= h >> 13;
        h *= M;
        h ^= h >> 15;
        return h;
    }

    public static uint Compute(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        return Compute(stream);
    }

    private static bool IsWhitespace(byte b) => b is 9 or 10 or 13 or 32;
}
