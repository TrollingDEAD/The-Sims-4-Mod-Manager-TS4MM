using Sims4ModManager.Core.Localization;
namespace Sims4ModManager.Core.Tray;

/// <summary>
/// Finds files the game will not pick up because they are in the wrong place: tray files in Mods
/// or in Tray subfolders, mods in Tray, and downloads that were never unpacked - the most common
/// reasons a downloaded household or lot does not show up. Also moves them to where they belong.
/// </summary>
public static class TrayHousekeeping
{
    public static IReadOnlyList<PlacementIssue> Inspect(string? trayPath, string? modsPath)
    {
        var issues = new List<PlacementIssue>();

        if (trayPath is not null && Directory.Exists(trayPath))
        {
            foreach (string file in SafeEnumerate(trayPath, SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                bool inSubfolder = !string.Equals(Path.GetDirectoryName(file), trayPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

                if (TrayInstaller.IsArchive(file))
                {
                    issues.Add(new PlacementIssue(PlacementIssueKind.ArchiveNotExtracted, file, null,
                        L.F("Nicht entpackter Download im Tray-Ordner: {0} – auswählen und „Archiv installieren“ klicken.", name)));
                }
                else if (ModFileNaming.IsManagedModFile(name))
                {
                    issues.Add(new PlacementIssue(PlacementIssueKind.ModFileInTray, file,
                        modsPath is null ? null : Path.Combine(modsPath, ModFileNaming.GetEffectiveFileName(name)),
                        L.F("{0} ist eine Mod-Datei und gehört in den Mods-Ordner – im Tray-Ordner wird sie ignoriert.", name)));
                }
                else if (TrayFileName.HasTrayExtension(name) && !TrayFileName.TryParse(name, out _))
                {
                    issues.Add(new PlacementIssue(PlacementIssueKind.RenamedTrayFile, file, null,
                        L.F("{0}: Tray-Dateiname wurde verändert – das Spiel erkennt die Datei so nicht.", name)));
                }
                else if (inSubfolder && TrayFileName.HasTrayExtension(name))
                {
                    issues.Add(new PlacementIssue(PlacementIssueKind.TrayFileInSubfolder, file, Path.Combine(trayPath, name),
                        L.F("{0} liegt im Unterordner „{1}“ – die Bibliothek liest nur den Tray-Ordner selbst.", name, Path.GetRelativePath(trayPath, Path.GetDirectoryName(file)!))));
                }
            }
        }

        if (modsPath is not null && Directory.Exists(modsPath))
        {
            foreach (string file in SafeEnumerate(modsPath, SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (TrayFileName.HasTrayExtension(name))
                {
                    issues.Add(new PlacementIssue(PlacementIssueKind.TrayFileInMods, file,
                        trayPath is null ? null : Path.Combine(trayPath, name),
                        L.F("{0} ist eine Tray-Datei (Bibliothek) und gehört in den Tray-Ordner – im Mods-Ordner wird sie ignoriert.", name)));
                }
                else if (TrayInstaller.IsArchive(file))
                {
                    issues.Add(new PlacementIssue(PlacementIssueKind.ArchiveNotExtracted, file, null,
                        L.F("Nicht entpackter Download im Mods-Ordner: {0} – auswählen und „Archiv installieren“ klicken.", Path.GetRelativePath(modsPath, file))));
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Moves a misplaced file to its suggested target. If an identical file is already there, the
    /// misplaced copy is moved to <paramref name="backupDirectory"/> instead of being deleted.
    /// Returns an error message, or null on success.
    /// </summary>
    public static string? TryFix(PlacementIssue issue, Backup.ChangeRecorder recorder)
    {
        if (issue.SuggestedTarget is null)
            return L.F("{0}: kann nicht automatisch verschoben werden.", Path.GetFileName(issue.Path));

        try
        {
            if (File.Exists(issue.SuggestedTarget))
            {
                if (!TrayInstaller.FilesEqual(issue.Path, issue.SuggestedTarget))
                    return L.F("{0}: am Ziel existiert bereits eine andere Datei mit diesem Namen.", Path.GetFileName(issue.Path));

                recorder.Delete(issue.Path); // identical copy already in place - keep the misplaced one in the backup
                return null;
            }

            recorder.Move(issue.Path, issue.SuggestedTarget);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{Path.GetFileName(issue.Path)}: {ex.Message}";
        }
    }

    internal static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        int i = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            i++;
        } while (File.Exists(candidate) || Directory.Exists(candidate));
        return candidate;
    }

    private static IEnumerable<string> SafeEnumerate(string dir, SearchOption option)
    {
        try { return Directory.GetFiles(dir, "*", option); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
