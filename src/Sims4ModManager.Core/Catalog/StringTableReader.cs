using System.Text;

namespace Sims4ModManager.Core.Catalog;

/// <summary>Reads string tables (STBL, type 0x220557DA): key hash → text.</summary>
public static class StringTableReader
{
    public const uint ResourceType = 0x220557DA;

    /// <summary>Language of a string table: the top byte of its instance (0x00 English, 0x08? German …).</summary>
    public static byte Language(ulong instance) => (byte)(instance >> 56);

    public static Dictionary<uint, string> TryRead(byte[] data)
    {
        var result = new Dictionary<uint, string>();
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            if (reader.ReadUInt32() != 0x4C425453) // "STBL"
                return result;
            ushort version = reader.ReadUInt16();
            if (version != 5)
                return result;
            reader.ReadByte();    // compressed
            ulong count = reader.ReadUInt64();
            reader.ReadUInt16();  // reserved
            reader.ReadUInt32();  // total string length
            for (ulong i = 0; i < count && reader.BaseStream.Position < reader.BaseStream.Length; i++)
            {
                uint key = reader.ReadUInt32();
                reader.ReadByte(); // flags
                ushort length = reader.ReadUInt16();
                result[key] = Encoding.UTF8.GetString(reader.ReadBytes(length));
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            // keep what was read
        }
        return result;
    }
}
