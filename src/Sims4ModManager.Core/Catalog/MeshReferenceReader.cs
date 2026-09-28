using System.Text;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Catalog;

/// <summary>
/// Reads which mesh a CAS part or an object uses, so recolors whose mesh is missing can be found.
/// <list type="bullet">
/// <item>CAS part (CASP): the second uint is the offset (from byte 8) of its key list - a count byte,
/// then (instance, group, type) per key; the meshes are the GEOM keys (one per level of detail).</item>
/// <item>Object definition (OBJD): uint16 version, uint32 offset of the property table (uint16 count,
/// then id/offset pairs); property 0x8D20ACC6 holds the model key as a word count and (instance as high/low
/// uint32, type, group),
/// property 0xE7F07786 the internal name.</item>
/// </list>
/// Both layouts were checked against real CC.
/// </summary>
public static class MeshReferenceReader
{
    public const uint Geometry = 0x015A1849;
    public const uint Model = 0x01661233;
    public const uint ObjectDefinition = 0xC0DB5AE7;

    private const uint ModelProperty = 0x8D20ACC6;
    private const uint NameProperty = 0xE7F07786;

    /// <summary>The GEOM keys of a CAS part (empty for parts without a mesh, e.g. make-up).</summary>
    public static IReadOnlyList<ResourceKey> ReadCasMeshes(byte[] casp)
    {
        var result = new List<ResourceKey>();
        if (casp.Length < 9)
            return result;
        long listPos = 8L + BitConverter.ToUInt32(casp, 4);
        if (listPos >= casp.Length)
            return result;
        int count = casp[listPos];
        long pos = listPos + 1;
        for (int i = 0; i < count && pos + 16 <= casp.Length; i++, pos += 16)
        {
            ulong instance = BitConverter.ToUInt64(casp, (int)pos);
            uint group = BitConverter.ToUInt32(casp, (int)pos + 8);
            uint type = BitConverter.ToUInt32(casp, (int)pos + 12);
            if (type == Geometry && instance != 0)
                result.Add(new ResourceKey(type, group, instance));
        }
        return result;
    }

    /// <summary>Model key and internal name of an object definition.</summary>
    public static (ResourceKey? Model, string? Name) ReadObject(byte[] objd)
    {
        try
        {
            if (objd.Length < 8)
                return (null, null);
            int table = (int)BitConverter.ToUInt32(objd, 2);
            if (table <= 0 || table + 2 > objd.Length)
                return (null, null);
            int count = BitConverter.ToUInt16(objd, table);
            ResourceKey? model = null;
            string? name = null;
            for (int i = 0; i < count; i++)
            {
                int entry = table + 2 + i * 8;
                if (entry + 8 > objd.Length)
                    break;
                uint id = BitConverter.ToUInt32(objd, entry);
                int offset = (int)BitConverter.ToUInt32(objd, entry + 4);
                if (offset < 0 || offset + 4 > objd.Length)
                    continue;
                if (id == ModelProperty && BitConverter.ToUInt32(objd, offset) >= 4 && offset + 20 <= objd.Length)
                {
                    // The instance is stored as two uint32 halves, high half first.
                    ulong instance = ((ulong)BitConverter.ToUInt32(objd, offset + 4) << 32) | BitConverter.ToUInt32(objd, offset + 8);
                    uint type = BitConverter.ToUInt32(objd, offset + 12);
                    uint group = BitConverter.ToUInt32(objd, offset + 16);
                    if (instance != 0 && type == Model)
                        model = new ResourceKey(type, group, instance);
                }
                else if (id == NameProperty)
                {
                    int length = (int)BitConverter.ToUInt32(objd, offset);
                    if (length is > 0 and < 512 && offset + 4 + length <= objd.Length)
                        name = Encoding.ASCII.GetString(objd, offset + 4, length);
                }
            }
            return (model, name);
        }
        catch (ArgumentException)
        {
            return (null, null);
        }
    }
}
