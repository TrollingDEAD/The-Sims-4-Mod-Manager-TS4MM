using System.Diagnostics;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Health;

/// <summary>
/// OneDrive is a frequent cause of "mods don't load": with "Files On-Demand" mods can be cloud-only
/// placeholders the game cannot read, and syncing locks files while the game runs.
/// </summary>
public static class OneDriveCheck
{
    // Placeholder attributes of cloud files (not all are in the FileAttributes enum).
    private const int RecallOnDataAccess = 0x400000;
    private const int RecallOnOpen = 0x40000;
    private const int Offline = 0x1000;

    public static IEnumerable<string> OneDriveRoots() =>
        new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.GetFullPath(r!).TrimEnd('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public static bool IsInOneDrive(string path, IEnumerable<string>? oneDriveRoots = null)
    {
        string full = Path.GetFullPath(path);
        return (oneDriveRoots ?? OneDriveRoots()).Any(root =>
            full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Files that are only stored in the cloud and have to be downloaded before the game can read them.</summary>
    public static IReadOnlyList<string> FindCloudOnlyFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => ((int)File.GetAttributes(f) & (RecallOnDataAccess | RecallOnOpen | Offline)) != 0)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static IEnumerable<HealthIssue> Inspect(string gameDataFolder, string modsPath)
    {
        if (!IsInOneDrive(gameDataFolder))
            yield break;

        var cloudOnly = FindCloudOnlyFiles(modsPath);
        if (cloudOnly.Count > 0)
        {
            yield return new HealthIssue
            {
                Id = "onedrive-cloud-only",
                Category = "OneDrive",
                Severity = HealthSeverity.Error,
                Title = L.T("Mods nur in der Cloud"),
                Description = L.F("{0} Mod-Datei(en) liegen nur in OneDrive und nicht auf dem PC – das Spiel kann sie nicht laden. ", cloudOnly.Count) +
                              L.T("„Immer auf diesem Gerät behalten“ lädt sie herunter und hält sie lokal."),
                Paths = cloudOnly,
                FixLabel = L.T("Auf diesem Gerät behalten"),
                Fix = _ => PinLocally(modsPath)
            };
        }

        yield return new HealthIssue
        {
            Id = "onedrive-sync",
            Category = "OneDrive",
            Severity = HealthSeverity.Warning,
            Title = L.T("Sims 4-Ordner liegt in OneDrive"),
            Description = L.T("OneDrive synchronisiert den Sims 4-Ordner. Das verursacht laut EA-Forum häufig nicht geladene Mods, gesperrte Dateien ") +
                          L.T("und volle Cloud-Speicher. Empfehlung: Dokumente-Ordner aus der OneDrive-Sicherung nehmen oder den Sims 4-Ordner ") +
                          L.T("„Immer auf diesem Gerät behalten“ und Sicherungen stattdessen hier im Tool anlegen."),
            Paths = new[] { gameDataFolder }
        };
    }

    /// <summary>Marks the folder as "always keep on this device" (attrib +P -U), which downloads cloud-only files.</summary>
    private static HealthFixResult PinLocally(string folder)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("attrib.exe", $"+P -U \"{Path.Combine(folder, "*")}\" /S /D")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit(120_000);
            return process is { ExitCode: 0 }
                ? HealthFixResult.From(1, new List<string>())
                : HealthFixResult.From(0, new List<string> { L.T("attrib konnte die Dateien nicht anheften.") });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return HealthFixResult.From(0, new List<string> { ex.Message });
        }
    }
}
