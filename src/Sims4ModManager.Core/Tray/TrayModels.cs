using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Tray;

/// <summary>Library item kinds, matching the game's exchange item type ids.</summary>
public enum TrayItemType
{
    Unknown = 0,
    Household = 1,
    Lot = 2,
    Room = 3
}

public sealed record TraySim(string FirstName, string LastName, ulong Id)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>What the .trayitem file says about a library item.</summary>
public sealed class TrayItemMetadata
{
    public required ulong Id { get; init; }
    public required TrayItemType Type { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CreatorName { get; init; } = string.Empty;
    public ulong Favorites { get; init; }
    public ulong Downloads { get; init; }
    public DateTime? CreatedUtc { get; init; }

    /// <summary>Hashtags the creator added in the gallery (lots/rooms), e.g. "noCC,noMods".</summary>
    public string Tags { get; init; } = string.Empty;

    public IReadOnlyList<TraySim> Sims { get; init; } = Array.Empty<TraySim>();

    /// <summary>Lot size in tiles (width, depth) - lots only.</summary>
    public (int Width, int Depth)? LotSize { get; init; }
}

/// <summary>
/// One entry of the in-game library ("Meine Bibliothek"): all Tray files that belong together.
/// Items without a readable .trayitem, or missing their main data file, are still listed so the
/// user can see (and clean up) broken downloads.
/// </summary>
public sealed class TrayItem
{
    public required ulong Id { get; init; }
    public required TrayItemType Type { get; init; }
    public TrayItemMetadata? Metadata { get; init; }
    public required IReadOnlyList<string> Files { get; init; }

    /// <summary>Reasons the game will not show/load this item correctly. Empty means complete.</summary>
    public required IReadOnlyList<string> Problems { get; init; }

    public bool IsComplete => Problems.Count == 0;
    public long TotalSizeBytes => Files.Sum(f => SafeLength(f));
    public DateTime LastWriteUtc => Files.Count == 0 ? DateTime.MinValue : Files.Max(File.GetLastWriteTimeUtc);

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Metadata?.Name) ? Metadata!.Name : L.F("(ohne Namen) 0x{0:x16}", Id);

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return 0; }
    }
}

public enum PlacementIssueKind
{
    /// <summary>Tray file inside the Mods folder - the game ignores it there.</summary>
    TrayFileInMods,
    /// <summary>.package/.ts4script inside Tray - the game ignores it there.</summary>
    ModFileInTray,
    /// <summary>Tray file in a subfolder of Tray - the library only reads the Tray folder itself.</summary>
    TrayFileInSubfolder,
    /// <summary>Download archive that still has to be unpacked.</summary>
    ArchiveNotExtracted,
    /// <summary>File named like tray data but not in the game's naming scheme (renamed?).</summary>
    RenamedTrayFile
}

/// <summary>A file lying where the game will not pick it up, with the fix if one exists.</summary>
public sealed record PlacementIssue(PlacementIssueKind Kind, string Path, string? SuggestedTarget, string Message);

/// <summary>A mod file providing resources referenced by a tray item.</summary>
public sealed record TrayCcReference(ModEntry Mod, ModFileInfo File, int ResourceCount, IReadOnlyList<string> Categories)
{
    public bool IsEnabled => File.IsEnabled;
}
