using Sims4ModManager.Core.Localization;
namespace Sims4ModManager.Core.Tray;

public sealed class TrayLibrary
{
    public static readonly TrayLibrary Empty = new() { TrayPath = null, Items = Array.Empty<TrayItem>() };

    public required string? TrayPath { get; init; }
    public required IReadOnlyList<TrayItem> Items { get; init; }
}

/// <summary>
/// Reads the Tray folder and groups its files into library items. Files are named
/// "0x{group}!0x{instance}.{ext}"; everything sharing an instance belongs to one item, except sim
/// portraits (.sgi), which are named after the sim's id and are matched via the household's metadata.
/// Only the Tray folder itself is read - like the game, subfolders are ignored (see <see cref="TrayHousekeeping"/>).
/// </summary>
public static class TrayLibraryScanner
{
    /// <summary>Data file each item type needs besides the .trayitem to be loadable.</summary>
    private static readonly Dictionary<TrayItemType, string> RequiredDataFile = new()
    {
        [TrayItemType.Household] = "householdbinary",
        [TrayItemType.Lot] = "blueprint",
        [TrayItemType.Room] = "room"
    };

    public static TrayLibrary Scan(string trayPath)
    {
        if (!Directory.Exists(trayPath))
            return new TrayLibrary { TrayPath = trayPath, Items = Array.Empty<TrayItem>() };

        var groups = new Dictionary<ulong, List<(string Path, TrayFileName Name)>>();
        foreach (string path in SafeGetFiles(trayPath))
        {
            if (!TrayFileName.TryParse(Path.GetFileName(path), out var name))
                continue;
            if (!groups.TryGetValue(name.Instance, out var list))
                groups[name.Instance] = list = new List<(string, TrayFileName)>();
            list.Add((path, name));
        }

        // Pass 1: items with a .trayitem; remember their sims to claim the portrait groups.
        var items = new Dictionary<ulong, (TrayItemMetadata? Metadata, List<string> Files, List<string> Problems, TrayItemType Type)>();
        var claimedBy = new Dictionary<ulong, ulong>(); // portrait instance -> household instance

        foreach (var (instance, files) in groups)
        {
            var trayItemFile = files.FirstOrDefault(f => f.Name.Extension == TrayFileName.TrayItemExtension);
            if (trayItemFile.Path is null)
                continue;

            var metadata = TrayMetadataReader.TryRead(trayItemFile.Path);
            var problems = new List<string>();
            var type = metadata?.Type ?? InferType(files);
            if (metadata is null)
                problems.Add(L.T(".trayitem-Datei ist beschädigt oder unlesbar."));

            items[instance] = (metadata, files.Select(f => f.Path).ToList(), problems, type);

            foreach (var sim in metadata?.Sims ?? Array.Empty<TraySim>())
            {
                if (sim.Id != 0 && sim.Id != instance && groups.ContainsKey(sim.Id))
                    claimedBy[sim.Id] = instance;
            }
        }

        // Pass 2: portrait-only groups not claimed via metadata - fall back to the id scheme
        // (sim id = household id + n, top byte used as an index).
        foreach (var (instance, files) in groups)
        {
            if (items.ContainsKey(instance) || claimedBy.ContainsKey(instance) || !files.All(f => f.Name.Extension == "sgi"))
                continue;

            ulong masked = instance & 0x00FF_FFFF_FFFF_FFFF;
            var owner = items
                .Where(kv => kv.Value.Type == TrayItemType.Household)
                .Select(kv => (Id: kv.Key, Delta: masked - (kv.Key & 0x00FF_FFFF_FFFF_FFFF)))
                .Where(x => x.Delta is >= 1 and <= 32)
                .OrderBy(x => x.Delta)
                .FirstOrDefault();
            if (owner.Id != 0)
                claimedBy[instance] = owner.Id;
        }

        foreach (var (portraitInstance, owner) in claimedBy)
            items[owner].Files.AddRange(groups[portraitInstance].Select(f => f.Path));

        // Pass 3: groups without .trayitem that nobody claimed - leftovers of incomplete downloads.
        var result = new List<TrayItem>();
        foreach (var (instance, files) in groups)
        {
            if (items.ContainsKey(instance) || claimedBy.ContainsKey(instance))
                continue;

            var type = InferType(files);
            result.Add(new TrayItem
            {
                Id = instance,
                Type = type,
                Metadata = null,
                Files = files.Select(f => f.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
                Problems = new[]
                {
                    files.All(f => f.Name.Extension == "sgi")
                        ? L.T("Sim-Porträt ohne zugehörigen Haushalt.")
                        : L.T("Keine .trayitem-Datei – der Eintrag erscheint nicht in der Bibliothek (Download unvollständig?).")
                }
            });
        }

        foreach (var (instance, item) in items)
        {
            if (RequiredDataFile.TryGetValue(item.Type, out var required)
                && !item.Files.Any(f => f.EndsWith("." + required, StringComparison.OrdinalIgnoreCase)))
            {
                item.Problems.Add(L.F("Die .{0}-Datei fehlt – der Eintrag kann nicht geladen werden (Download unvollständig?).", required));
            }

            result.Add(new TrayItem
            {
                Id = instance,
                Type = item.Type,
                Metadata = item.Metadata,
                Files = item.Files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
                Problems = item.Problems
            });
        }

        return new TrayLibrary
        {
            TrayPath = trayPath,
            Items = result
                .OrderBy(i => i.Type == TrayItemType.Unknown)
                .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    private static TrayItemType InferType(List<(string Path, TrayFileName Name)> files)
    {
        foreach (var (type, extension) in RequiredDataFile)
        {
            if (files.Any(f => f.Name.Extension == extension))
                return type;
        }
        if (files.Any(f => f.Name.Extension is "hhi" or "sgi")) return TrayItemType.Household;
        if (files.Any(f => f.Name.Extension == "bpi")) return TrayItemType.Lot;
        if (files.Any(f => f.Name.Extension == "rmi")) return TrayItemType.Room;
        return TrayItemType.Unknown;
    }

    private static string[] SafeGetFiles(string dir)
    {
        try { return Directory.GetFiles(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
