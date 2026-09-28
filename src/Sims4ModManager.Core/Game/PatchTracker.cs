using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Game;

/// <param name="PreviousVersion">Version before the update, null if the tool has not seen one yet.</param>
/// <param name="UpdatedUtc">When the game first ran the new version (GameVersion.txt timestamp).</param>
public sealed record PatchInfo(string CurrentVersion, string? PreviousVersion, DateTime? UpdatedUtc, bool IsNewSinceLastCheck);

/// <summary>A mod file that is likely affected by a game update.</summary>
public sealed record AtRiskFile(ModEntry Mod, ModFileInfo File, string Reason);

/// <summary>
/// Detects game updates and lists the mods most likely to break by them: script mods and packages
/// that override game logic (tuning, SimData), when the file is older than the update. CC like hair
/// or furniture rarely breaks and is left alone.
/// </summary>
public static class PatchTracker
{
    /// <summary>
    /// Gameplay tuning that tends to break with patches. Object tuning and SimData are deliberately
    /// missing: every custom object ships them, and furniture CC rarely breaks.
    /// </summary>
    private static readonly HashSet<uint> LogicTypes = new()
    {
        0x0333406C, 0x03B33DDF, 0x62E94D38,              // XML tuning, tuning, combined tuning
        0x6017E896, 0xCB5FDDC7, 0xE882D22F, 0x0C772E27, 0x7DF2169C // buff, trait, interaction, loot, snippet
    };

    /// <summary>Compares the installed version with the one seen last time.</summary>
    public static PatchInfo? Check(string gameDataFolder, string? lastSeenVersion)
    {
        string? current = GameInfo.TryReadGameVersion(gameDataFolder);
        if (current is null)
            return null;

        bool isNew = lastSeenVersion is not null && GameInfo.CompareVersions(current, lastSeenVersion) > 0;
        return new PatchInfo(current, lastSeenVersion, GameInfo.TryGetVersionTimestampUtc(gameDataFolder), isNew);
    }

    /// <summary>Enabled script mods and logic-overriding packages last changed before <paramref name="updatedUtc"/>.</summary>
    public static IReadOnlyList<AtRiskFile> FindAtRisk(IEnumerable<ModEntry> mods, DateTime updatedUtc)
    {
        var result = new List<AtRiskFile>();
        foreach (var mod in mods)
        {
            foreach (var file in mod.Files.Where(f => f.IsEnabled))
            {
                DateTime written;
                try { written = System.IO.File.GetLastWriteTimeUtc(file.AbsolutePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if (written >= updatedUtc)
                    continue;

                if (file.Kind == ModFileKind.Script)
                {
                    result.Add(new AtRiskFile(mod, file, L.T("Skript-Mod älter als das Update")));
                    continue;
                }

                int logic = file.Resources.Count(r => LogicTypes.Contains(r.Key.Type));
                if (logic > 0)
                    result.Add(new AtRiskFile(mod, file, L.F("Ändert Spiel-Logik ({0} Tuning-Ressourcen), älter als das Update", logic)));
            }
        }
        return result;
    }

    /// <summary>EA forum search for the community's "Broken and Updated Mods" thread of this version.</summary>
    public static string BrokenModsListUrl(string version) =>
        "https://forums.ea.com/category/the-sims-4-en/discussions/the-sims-4-mods-and-custom-content-en?q=" +
        Uri.EscapeDataString(L.F("Broken and Updated Sims 4 Mods patch {0}", GameInfo.ShortVersion(version)));
}
