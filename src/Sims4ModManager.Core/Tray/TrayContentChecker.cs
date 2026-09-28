using System.Collections.Concurrent;
using Sims4ModManager.Core.Game;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Tray;

/// <summary>What a library item needs from the game: packs, and content that is nowhere to be found.</summary>
/// <param name="PacksComplete">
/// True when the item's data could be read field by field (households); otherwise the packs are only
/// those found by scanning, and there may be more.
/// </param>
public sealed record TrayGameCheck(
    IReadOnlyList<GamePack> RequiredPacks,
    bool PacksComplete,
    int MissingCount,
    IReadOnlyList<string> MissingCategories,
    bool MissingCheckSupported)
{
    public static readonly TrayGameCheck None = new(Array.Empty<GamePack>(), false, 0, Array.Empty<string>(), false);
}

/// <summary>
/// Compares library items with the <see cref="GameIndex"/> and the mods.
/// <para>
/// <b>Households:</b> the household protobuf is walked field by field. Field paths whose 64-bit values
/// resolve to game or mod catalog items at least 80 % of the time (across all households) are catalog
/// references - an outfit's CAS parts, for example. Every value at such a path is looked up: game items
/// give the packs needed, values that resolve to nothing belong to CC that is not installed (or to a pack
/// that is not installed). Learning the paths from the data avoids depending on the undocumented layout.
/// Paths are learned from 64-bit values only, since small numbers resolve by chance too often.
/// </para>
/// <para>
/// <b>Lots and rooms</b> are stored in a compressed format; the raw bytes are only scanned for 64-bit ids
/// (as in <see cref="TrayCcAnalyzer"/>). Game objects mostly have small ids, so the packs found this way
/// are a lower bound, and missing CC cannot be told apart from other data.
/// </para>
/// </summary>
public sealed class TrayContentChecker
{
    private const double ReferencePathShare = 0.8;
    private const int MinResolvedPerPath = 3;

    private readonly GameIndex _game;
    private readonly Dictionary<ulong, string> _modItems = new();

    public TrayContentChecker(GameIndex game, IEnumerable<ModEntry> mods)
    {
        _game = game;
        var catalogResources = mods.SelectMany(m => m.Files).SelectMany(f => f.Resources)
            .Where(r => r.Key.Instance > uint.MaxValue);
        foreach (var resource in catalogResources)
            if (TrayCcAnalyzer.ReferenceTypes.TryGetValue(resource.Key.Type, out var category))
                _modItems.TryAdd(resource.Key.Instance, category);
    }

    public IReadOnlyDictionary<TrayItem, TrayGameCheck> CheckAll(IReadOnlyCollection<TrayItem> items)
    {
        var scanned = new ConcurrentDictionary<TrayItem, HashSet<GamePack>>();
        var references = new ConcurrentDictionary<TrayItem, List<(string Path, ulong Value)>>();
        Parallel.ForEach(items, item =>
        {
            var found = new HashSet<GamePack>();
            var paths = new List<(string, ulong)>();
            foreach (string file in item.Files)
            {
                string extension = Path.GetExtension(file).TrimStart('.');
                if (!TrayFileName.DataExtensions.Contains(extension))
                    continue;
                byte[] data;
                try { data = File.ReadAllBytes(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                ScanForPacks(data, found);
                if (extension.Equals("householdbinary", StringComparison.OrdinalIgnoreCase))
                    paths.AddRange(HouseholdValues(data));
            }
            scanned[item] = found;
            references[item] = paths;
        });

        var referencePaths = LearnReferencePaths(references.Values.SelectMany(v => v));
        var result = new Dictionary<TrayItem, TrayGameCheck>();
        foreach (var item in items)
        {
            var values = references.GetValueOrDefault(item) ?? new List<(string, ulong)>();
            var atReferences = values.Where(v => referencePaths.ContainsKey(v.Path)).ToList();
            bool parsed = item.Type == TrayItemType.Household && atReferences.Count > 0;

            var packs = scanned.GetValueOrDefault(item) ?? new HashSet<GamePack>();
            foreach (var (_, value) in atReferences)
                if (_game.PackOfCatalogItem(value) is { } pack)
                    packs.Add(pack);

            // CC ids are 64-bit hashes; small unresolved numbers are old game ids or other data.
            var missing = atReferences.Where(v => v.Value > uint.MaxValue && !Resolves(v.Value))
                .Select(v => (v.Value, Category: referencePaths[v.Path])).DistinctBy(v => v.Value).ToList();
            result[item] = new TrayGameCheck(
                packs.Where(p => !p.IsBaseGame).OrderBy(p => GamePacks.SortKey(p.Code)).ThenBy(p => p.Code, StringComparer.Ordinal).ToList(),
                parsed, missing.Count, missing.Select(m => m.Category).Distinct().Order().ToList(), parsed);
        }
        return result;
    }

    private bool Resolves(ulong value) => _game.ContainsCatalogItem(value) || _modItems.ContainsKey(value);

    /// <summary>Paths whose 64-bit values mostly resolve, with the category most of their resolved values have.</summary>
    private Dictionary<string, string> LearnReferencePaths(IEnumerable<(string Path, ulong Value)> values)
    {
        var result = new Dictionary<string, string>();
        foreach (var group in values.Where(v => v.Value > uint.MaxValue).Distinct().GroupBy(v => v.Path))
        {
            var resolved = group.Where(v => Resolves(v.Value)).ToList();
            if (resolved.Count < MinResolvedPerPath || resolved.Count < group.Count() * ReferencePathShare)
                continue;
            result[group.Key] = resolved
                .Select(v => _modItems.TryGetValue(v.Value, out var c) ? c : GameCatalogCategory)
                .GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        }
        return result;
    }

    /// <summary>The game index only knows that an id is a catalog item; in households these are CAS parts.</summary>
    private const string GameCatalogCategory = "CAS";

    private void ScanForPacks(byte[] data, HashSet<GamePack> found)
    {
        for (int i = 0; i < data.Length; i++)
        {
            int pos = i;
            if (ProtoReader.TryReadVarint(data, ref pos, data.Length, out ulong varint) && varint > uint.MaxValue
                && _game.PackOfCatalogItem(varint) is { } pack)
                found.Add(pack);
            if (i + 8 <= data.Length)
            {
                ulong raw = BitConverter.ToUInt64(data, i);
                if (raw > uint.MaxValue && _game.PackOfCatalogItem(raw) is { } rawPack)
                    found.Add(rawPack);
            }
        }
    }


    /// <summary>A .householdbinary: uint32 version, uint32 size, uint32 0, uint32 message length, then the protobuf message.</summary>
    internal static IEnumerable<(string Path, ulong Value)> HouseholdValues(byte[] data)
    {
        var found = new List<(string, ulong)>();
        if (data.Length < 16)
            return found;
        int length = BitConverter.ToInt32(data, 12);
        if (length <= 0 || length > data.Length - 16)
            length = data.Length - 16;
        if (IsMessage(data, 16, length))
            Walk(data, 16, length, "", 0, found);
        return found;
    }

    /// <summary>Collects all non-zero numbers (varints, fixed64, packed varints) with their field path.</summary>
    internal static void Walk(byte[] data, int offset, int length, string path, int depth, List<(string, ulong)> found)
    {
        foreach (var field in ProtoReader.ReadFields(data, offset, length))
        {
            string fieldPath = path + "/" + field.Number;
            switch (field.Type)
            {
                case ProtoReader.WireType.Varint when field.Value > 0:
                    found.Add((fieldPath, field.Value));
                    break;
                case ProtoReader.WireType.Fixed64 when field.Value > 0:
                    found.Add((fieldPath + "f", field.Value));
                    break;
                case ProtoReader.WireType.LengthDelimited when field.Length > 0:
                    if (depth < 24 && IsMessage(data, field.Offset, field.Length))
                        Walk(data, field.Offset, field.Length, fieldPath, depth + 1, found);
                    else if (TryReadPacked(data, field.Offset, field.Length) is { } packed)
                        found.AddRange(packed.Where(v => v > 0).Select(v => (fieldPath + "p", v)));
                    break;
            }
        }
    }

    /// <summary>True if the bytes parse completely as protobuf fields.</summary>
    internal static bool IsMessage(byte[] data, int offset, int length)
    {
        int end = offset + length, pos = offset;
        if (length <= 0)
            return false;
        while (pos < end)
        {
            if (!ProtoReader.TryReadVarint(data, ref pos, end, out ulong tag) || (tag >> 3) is 0 or > 10_000)
                return false;
            switch (tag & 7)
            {
                case 0:
                    if (!ProtoReader.TryReadVarint(data, ref pos, end, out _))
                        return false;
                    break;
                case 1:
                    pos += 8;
                    break;
                case 5:
                    pos += 4;
                    break;
                case 2:
                    if (!ProtoReader.TryReadVarint(data, ref pos, end, out ulong len) || len > (ulong)(end - pos))
                        return false;
                    pos += (int)len;
                    break;
                default:
                    return false;
            }
        }
        return pos == end;
    }

    private static List<ulong>? TryReadPacked(byte[] data, int offset, int length)
    {
        var values = new List<ulong>();
        int pos = offset, end = offset + length;
        while (pos < end)
        {
            if (!ProtoReader.TryReadVarint(data, ref pos, end, out ulong value))
                return null;
            values.Add(value);
        }
        return values;
    }
}
