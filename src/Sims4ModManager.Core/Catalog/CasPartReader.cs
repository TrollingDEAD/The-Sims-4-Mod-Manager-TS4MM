using System.Text;

namespace Sims4ModManager.Core.Catalog;

/// <summary>The parts of a CAS part (CASP) resource the catalog needs.</summary>
public sealed record CasPartInfo(string Name, int BodyType, uint AgeGender, uint TitleKey)
{
    public const uint AgeInfant = 0x80, AgeToddler = 0x02, AgeChild = 0x04, AgeTeen = 0x08,
        AgeYoungAdult = 0x10, AgeAdult = 0x20, AgeElder = 0x40;
    public const uint GenderMale = 0x1000, GenderFemale = 0x2000;
}

/// <summary>
/// Reads the header of a CAS part resource (type 0x034AEECB) up to the body type and age/gender flags.
/// The flag block between the parameter flags and the tag list grew over the game's versions; its
/// size per version was measured on real CC (v40–v52). For newer versions the tag list is located by
/// its structure, so a future format change degrades to "no details" instead of wrong ones.
/// </summary>
public static class CasPartReader
{
    public const uint ResourceType = 0x034AEECB;

    /// <summary>Bytes from the parameter flags up to the tag count, by CASP version.</summary>
    private static int? FlagBlockSize(uint version) => version switch
    {
        >= 40 and <= 40 => 18,
        >= 41 and <= 49 => 26,
        50 => 28,
        51 or 52 => 40,
        _ => null
    };

    public static CasPartInfo? TryRead(byte[] data)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            uint version = reader.ReadUInt32();
            if (version is < 40 or > 0x100)
                return null;
            reader.ReadUInt32(); // offset of the resource key list
            if (reader.ReadUInt32() != 0)
                return null; // presets are rare in CC and would need a full parse to skip
            string name = ReadBigEndianString(reader);
            reader.BaseStream.Seek(4 + 2 + 4 + 4, SeekOrigin.Current); // sort priority, secondary sort index, property id, aural material

            long flagsStart = reader.BaseStream.Position;
            long? tagCountPos = FlagBlockSize(version) is int size ? flagsStart + size : FindTagList(data, flagsStart);
            if (tagCountPos is null)
                return null;
            reader.BaseStream.Position = tagCountPos.Value;

            uint tagCount = reader.ReadUInt32();
            if (tagCount > 2_000)
                return null;
            reader.BaseStream.Seek(tagCount * 6L, SeekOrigin.Current); // category (ushort) + value (uint)

            reader.ReadUInt32(); // price (unused)
            uint titleKey = reader.ReadUInt32();
            reader.ReadUInt32(); // description key
            if (version >= 43)
                reader.ReadUInt32(); // create description key
            reader.ReadByte();   // unique texture space
            int bodyType = reader.ReadInt32();
            reader.ReadInt32();  // body sub type
            uint ageGender = reader.ReadUInt32();

            // A misaligned read shows up as nonsense here - better no details than wrong ones.
            if (bodyType is < 0 or > 200 || (ageGender & 0xFFFF_C000) != 0)
                return null;
            return new CasPartInfo(name, bodyType, ageGender, titleKey);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Finds the tag list of an unknown version: a count followed by that many (category, value) pairs.</summary>
    private static long? FindTagList(byte[] data, long flagsStart)
    {
        for (long i = flagsStart + 1; i < flagsStart + 96 && i + 4 <= data.Length; i++)
        {
            uint count = BitConverter.ToUInt32(data, (int)i);
            if (count is 0 or > 500 || i + 4 + count * 6 > data.Length)
                continue;
            bool plausible = true;
            for (int t = 0; t < count && plausible; t++)
            {
                ushort category = BitConverter.ToUInt16(data, (int)(i + 4 + t * 6));
                plausible = category is > 0 and < 0x400;
            }
            if (plausible)
                return i;
        }
        return null;
    }

    /// <summary>7-bit encoded byte length followed by UTF-16 big-endian text.</summary>
    private static string ReadBigEndianString(BinaryReader reader)
    {
        int length = reader.Read7BitEncodedInt();
        if (length is < 0 or > 4096)
            throw new ArgumentException("Implausible name length.");
        return Encoding.BigEndianUnicode.GetString(reader.ReadBytes(length));
    }
}
