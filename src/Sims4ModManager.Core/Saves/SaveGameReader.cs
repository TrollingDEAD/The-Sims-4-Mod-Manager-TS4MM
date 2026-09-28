using System.IO.Compression;
using System.Text.RegularExpressions;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Tray;

namespace Sims4ModManager.Core.Saves;

/// <summary>A save slot: the main file plus the game's automatic older versions (.ver0-.ver4, .day.ver0).</summary>
public sealed class SaveGameInfo
{
    public required string Path { get; init; }
    public required int Slot { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? SavedWithVersion { get; init; }
    public string? CreatedWithVersion { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public long SizeBytes { get; init; }
    public IReadOnlyList<string> HouseholdNames { get; init; } = Array.Empty<string>();
    public int SimCount { get; init; }
    public int LotCount { get; init; }
    public IReadOnlyList<string> VersionFiles { get; init; } = Array.Empty<string>();
    public bool IsReadable { get; init; } = true;
}

/// <summary>
/// Reads Sims 4 save files (DBPF packages). The overview lives in the SaveGameData resource
/// (type 0x0000000D, protobuf, usually RefPack-compressed). Field numbers were verified against
/// real saves: 3 = slot info (2 name, 10 game version when saved, 17 when created),
/// 5 = households (3 name), 6 = sims, 7 = lots.
/// </summary>
public static class SaveGameReader
{
    private const uint SaveGameDataType = 0x0000000D;
    private static readonly Regex SlotPattern = new(@"^Slot_(?<slot>[0-9a-fA-F]{8})\.save$", RegexOptions.Compiled);

    public static IReadOnlyList<SaveGameInfo> List(string gameDataFolder)
    {
        string dir = System.IO.Path.Combine(gameDataFolder, "saves");
        if (!Directory.Exists(dir))
            return Array.Empty<SaveGameInfo>();

        var all = Directory.GetFiles(dir, "Slot_*");
        var saves = new List<SaveGameInfo>();
        foreach (string file in all)
        {
            var match = SlotPattern.Match(System.IO.Path.GetFileName(file));
            if (!match.Success)
                continue;
            int slot = Convert.ToInt32(match.Groups["slot"].Value, 16);
            var versions = all.Where(f => f.StartsWith(file + ".", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            saves.Add(Read(file, slot, versions));
        }
        return saves.OrderByDescending(s => s.LastWriteUtc).ToList();
    }

    private static SaveGameInfo Read(string path, int slot, IReadOnlyList<string> versions)
    {
        var info = new FileInfo(path);
        var baseInfo = new SaveGameInfo { Path = path, Slot = slot, LastWriteUtc = info.LastWriteTimeUtc, SizeBytes = info.Length, VersionFiles = versions };
        try
        {
            var index = DbpfReader.TryReadIndex(path);
            var dataResource = index?.FirstOrDefault(r => r.Key.Type == SaveGameDataType);
            if (index is null || dataResource is null || dataResource.Value.StoredSize == 0)
                return Unreadable(baseInfo);

            using var stream = File.OpenRead(path);
            byte[] data = ReadResource(stream, dataResource.Value);
            return ParseSaveGameData(data, baseInfo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Unreadable(baseInfo);
        }
    }

    private static SaveGameInfo ParseSaveGameData(byte[] data, SaveGameInfo baseInfo)
    {
        string name = string.Empty;
        string? saved = null, created = null;
        var households = new List<string>();
        int sims = 0, lots = 0;

        foreach (var field in ProtoReader.ReadFields(data, 0, data.Length))
        {
            switch (field.Number)
            {
                case 3 when field.Type == ProtoReader.WireType.LengthDelimited:
                    foreach (var f in ProtoReader.ReadFields(data, field.Offset, field.Length))
                    {
                        if (f.Type != ProtoReader.WireType.LengthDelimited) continue;
                        if (f.Number == 2) name = ProtoReader.ReadString(data, f);
                        else if (f.Number == 10) saved = ProtoReader.ReadString(data, f);
                        else if (f.Number == 17) created = ProtoReader.ReadString(data, f);
                    }
                    break;
                case 5 when field.Type == ProtoReader.WireType.LengthDelimited:
                    foreach (var f in ProtoReader.ReadFields(data, field.Offset, field.Length))
                    {
                        if (f.Number == 3 && f.Type == ProtoReader.WireType.LengthDelimited)
                        {
                            string household = ProtoReader.ReadString(data, f);
                            if (household.Length > 0)
                                households.Add(household);
                            break;
                        }
                    }
                    break;
                case 6: sims++; break;
                case 7: lots++; break;
            }
        }

        return new SaveGameInfo
        {
            Path = baseInfo.Path,
            Slot = baseInfo.Slot,
            LastWriteUtc = baseInfo.LastWriteUtc,
            SizeBytes = baseInfo.SizeBytes,
            VersionFiles = baseInfo.VersionFiles,
            Name = name,
            SavedWithVersion = saved,
            CreatedWithVersion = created,
            HouseholdNames = households,
            SimCount = sims,
            LotCount = lots
        };
    }

    private static SaveGameInfo Unreadable(SaveGameInfo baseInfo) => new()
    {
        Path = baseInfo.Path,
        Slot = baseInfo.Slot,
        LastWriteUtc = baseInfo.LastWriteUtc,
        SizeBytes = baseInfo.SizeBytes,
        VersionFiles = baseInfo.VersionFiles,
        IsReadable = false
    };

    /// <summary>
    /// CC used in a save: every resource is decompressed and scanned for instance ids of CC in the
    /// Mods folder - the same approach as for library items. Takes a second or two per save.
    /// </summary>
    public static IReadOnlyList<TrayCcReference> FindUsedCc(string savePath, TrayCcAnalyzer analyzer)
    {
        var index = DbpfReader.TryReadIndex(savePath);
        if (index is null)
            return Array.Empty<TrayCcReference>();

        using var stream = File.OpenRead(savePath);
        return analyzer.AnalyzeData(ReadAll(stream, index));
    }

    private static IEnumerable<byte[]> ReadAll(FileStream stream, IReadOnlyList<Models.PackageResource> index)
    {
        foreach (var resource in index)
        {
            byte[]? data;
            try { data = ReadResource(stream, resource); }
            catch (InvalidDataException) { data = null; }
            if (data is not null)
                yield return data;
        }
    }

    internal static byte[] ReadResource(FileStream stream, Models.PackageResource resource) => DbpfReader.ReadResource(stream, resource);
}
