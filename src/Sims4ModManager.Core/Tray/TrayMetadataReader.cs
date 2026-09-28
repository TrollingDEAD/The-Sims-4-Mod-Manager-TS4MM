namespace Sims4ModManager.Core.Tray;

/// <summary>
/// Reads .trayitem files: an 8-byte header (uint32 version, uint32 payload length) followed by the
/// game's TrayMetadata protobuf message. Field numbers were verified against real library files:
/// 1 id, 2 type, 4 name, 5 description, 7 creator name, 8 favorites, 9 downloads,
/// 10 type-specific data (1 lot, 2 household, 3 room), 11 timestamp (seconds since 0001-01-01).
/// </summary>
public static class TrayMetadataReader
{
    private const int HeaderSize = 8;

    public static TrayItemMetadata? TryRead(string path)
    {
        try
        {
            return TryParse(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static TrayItemMetadata? TryParse(byte[] data)
    {
        if (data.Length < HeaderSize)
            return null;

        int declared = (int)Math.Min(BitConverter.ToUInt32(data, 4), int.MaxValue);
        int length = declared > 0 && declared <= data.Length - HeaderSize ? declared : data.Length - HeaderSize;

        ulong? id = null;
        var type = TrayItemType.Unknown;
        string name = "", description = "", creator = "", tags = "";
        ulong favorites = 0, downloads = 0;
        DateTime? created = null;
        var sims = new List<TraySim>();
        (int, int)? lotSize = null;

        foreach (var field in ProtoReader.ReadFields(data, HeaderSize, length))
        {
            switch (field.Number)
            {
                case 1 when field.Type == ProtoReader.WireType.Varint: id = field.Value; break;
                case 2 when field.Type == ProtoReader.WireType.Varint: type = ToItemType(field.Value); break;
                case 4 when IsBytes(field): name = ProtoReader.ReadString(data, field); break;
                case 5 when IsBytes(field): description = ProtoReader.ReadString(data, field); break;
                case 7 when IsBytes(field): creator = ProtoReader.ReadString(data, field); break;
                case 8 when field.Type == ProtoReader.WireType.Varint: favorites = field.Value; break;
                case 9 when field.Type == ProtoReader.WireType.Varint: downloads = field.Value; break;
                case 10 when IsBytes(field):
                    ReadSpecificData(data, field, sims, ref tags, ref lotSize);
                    break;
                case 11 when field.Type == ProtoReader.WireType.Varint: created = ToDate(field.Value); break;
            }
        }

        if (id is null)
            return null; // not a tray metadata message

        return new TrayItemMetadata
        {
            Id = id.Value,
            Type = type,
            Name = name,
            Description = description,
            CreatorName = creator,
            Favorites = favorites,
            Downloads = downloads,
            CreatedUtc = created,
            Tags = tags,
            Sims = sims,
            LotSize = lotSize
        };
    }

    private static void ReadSpecificData(byte[] data, ProtoReader.Field specific, List<TraySim> sims,
        ref string tags, ref (int, int)? lotSize)
    {
        foreach (var field in ProtoReader.ReadFields(data, specific.Offset, specific.Length))
        {
            if (field.Number == 1 && IsBytes(field))
                lotSize = ReadLotSize(data, field);
            else if (field.Number == 2 && IsBytes(field))
                ReadHousehold(data, field, sims);
            else if (field.Number == 8 && IsBytes(field))
                tags = ProtoReader.ReadString(data, field);
        }
    }

    /// <summary>Lot data: fields 2/3 hold width and depth in tiles.</summary>
    private static (int, int)? ReadLotSize(byte[] data, ProtoReader.Field lot)
    {
        ulong width = 0, depth = 0;
        foreach (var field in ProtoReader.ReadFields(data, lot.Offset, lot.Length))
        {
            if (field.Number == 2 && field.Type == ProtoReader.WireType.Varint) width = field.Value;
            if (field.Number == 3 && field.Type == ProtoReader.WireType.Varint) depth = field.Value;
        }
        return width is > 0 and <= 256 && depth is > 0 and <= 256 ? ((int)width, (int)depth) : null;
    }

    /// <summary>Household data: repeated field 2 = sim (3 first name, 4 last name, 5 sim id).</summary>
    private static void ReadHousehold(byte[] data, ProtoReader.Field household, List<TraySim> sims)
    {
        foreach (var simField in ProtoReader.ReadFields(data, household.Offset, household.Length))
        {
            if (simField.Number != 2 || !IsBytes(simField))
                continue;

            string first = "", last = "";
            ulong simId = 0;
            foreach (var f in ProtoReader.ReadFields(data, simField.Offset, simField.Length))
            {
                if (f.Number == 3 && IsBytes(f)) first = ProtoReader.ReadString(data, f);
                else if (f.Number == 4 && IsBytes(f)) last = ProtoReader.ReadString(data, f);
                else if (f.Number == 5 && f.Type == ProtoReader.WireType.Varint) simId = f.Value;
            }

            if (simId != 0 || first.Length > 0 || last.Length > 0)
                sims.Add(new TraySim(first, last, simId));
        }
    }

    private static bool IsBytes(ProtoReader.Field field) => field.Type == ProtoReader.WireType.LengthDelimited;

    private static TrayItemType ToItemType(ulong value) => value switch
    {
        1 => TrayItemType.Household,
        2 => TrayItemType.Lot,
        3 => TrayItemType.Room,
        _ => TrayItemType.Unknown
    };

    private static DateTime? ToDate(ulong secondsSinceYearOne)
    {
        const ulong maxSeconds = 315_537_897_599; // DateTime.MaxValue in seconds
        if (secondsSinceYearOne == 0 || secondsSinceYearOne > maxSeconds)
            return null;
        return DateTime.SpecifyKind(DateTime.MinValue.AddSeconds(secondsSinceYearOne), DateTimeKind.Utc);
    }
}
