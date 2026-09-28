using Sims4ModManager.Core.Game;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Health;

public sealed class HealthReport
{
    public required IReadOnlyList<HealthIssue> Issues { get; init; }
    public required string? GameVersion { get; init; }
    public required PatchInfo? Patch { get; init; }
    public required IReadOnlyList<AtRiskFile> AtRiskAfterPatch { get; init; }
    public required GameOptions? Options { get; init; }
    public required CacheStatus? Cache { get; init; }
    public required bool GameRunning { get; init; }

    public int ErrorCount => Issues.Count(i => i.Severity == HealthSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == HealthSeverity.Warning);
    public int FixableCount => Issues.Count(i => i.CanFix);
}

/// <summary>
/// Runs every check (game settings, patch, cache, OneDrive, folder rules) and returns the findings
/// ordered by severity - the data behind the "Übersicht" page.
/// </summary>
public static class HealthCheck
{
    /// <summary>Key identifying a script file for the "trusted" list (name + size: a changed file loses the trust).</summary>
    public static string TrustKey(ModFileInfo file) =>
        $"{ModFileNaming.GetEffectiveFileName(Path.GetFileName(file.AbsolutePath))}|{file.SizeBytes}";

    public static HealthReport Run(string modsPath, IReadOnlyList<ModEntry> mods, string? lastSeenGameVersion,
        IReadOnlyCollection<string>? trustedScripts = null, Action<string>? trustScript = null)
    {
        var issues = new List<HealthIssue>();
        string? dataFolder = ModsFolderLocator.TryGetGameDataFolder(modsPath)?.Path;
        bool running = GameInfo.IsGameRunning();

        GameOptions? options = null;
        PatchInfo? patch = null;
        IReadOnlyList<AtRiskFile> atRisk = Array.Empty<AtRiskFile>();
        CacheStatus? cache = null;

        if (running)
            issues.Add(new HealthIssue
            {
                Id = "game-running",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Warning,
                Title = L.T("Sims 4 läuft gerade"),
                Description = L.T("Änderungen an Mods wirken erst nach einem Neustart des Spiels und schlagen bei geöffneten Dateien fehl. ") +
                              L.T("Am besten das Spiel vorher beenden.")
            });

        if (dataFolder is not null)
        {
            options = GameOptions.TryLoad(dataFolder);
            if (options is not null)
                issues.AddRange(CheckOptions(dataFolder, options, mods));

            patch = PatchTracker.Check(dataFolder, lastSeenGameVersion);
            if (patch?.UpdatedUtc is { } updated)
            {
                atRisk = PatchTracker.FindAtRisk(mods, updated);
                if (atRisk.Count > 0 && patch.IsNewSinceLastCheck)
                    issues.Add(PatchIssue(patch, atRisk));
            }

            cache = CacheCleaner.GetStatus(dataFolder, mods);
            if (cache.IsStale && !running)
            {
                string folder = dataFolder;
                issues.Add(new HealthIssue
                {
                    Id = "cache-stale",
                    Category = L.T("Spiel"),
                    Severity = HealthSeverity.Info,
                    Title = L.T("Spiel-Cache veraltet"),
                    Description = L.T("Seit der letzten Leerung des Caches wurden Mods geändert. Alte Vorschaubilder oder fehlender CC im Spiel ") +
                                  L.T("lassen sich durch Leeren beheben – das Spiel baut den Cache beim nächsten Start neu auf."),
                    FixLabel = L.T("Cache leeren"),
                    IsSafeToFixInBulk = true,
                    Fix = r => HealthFixResult.From(CacheCleaner.Clear(folder, r), new List<string>())
                });
            }

            issues.AddRange(OneDriveCheck.Inspect(dataFolder, modsPath));
        }

        issues.AddRange(ModFolderHealth.Inspect(modsPath));
        issues.AddRange(CheckDependencies(mods));
        issues.AddRange(CheckScripts(mods, trustedScripts ?? Array.Empty<string>(), trustScript));

        return new HealthReport
        {
            Issues = issues.OrderByDescending(i => i.Severity).ToList(),
            GameVersion = patch?.CurrentVersion,
            Patch = patch,
            AtRiskAfterPatch = atRisk,
            Options = options,
            Cache = cache,
            GameRunning = running
        };
    }

    private static IEnumerable<HealthIssue> CheckOptions(string dataFolder, GameOptions options, IReadOnlyList<ModEntry> mods)
    {
        if (options.ModsEnabled == false)
            yield return new HealthIssue
            {
                Id = "mods-disabled-in-game",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Error,
                Title = L.T("Mods sind im Spiel ausgeschaltet"),
                Description = L.T("In den Spieloptionen ist „Benutzerdefinierte Inhalte und Mods aktivieren“ aus – das passiert oft nach Updates. ") +
                              L.T("Kein Mod und kein CC wird geladen."),
                FixLabel = L.T("Im Spiel einschalten"),
                IsSafeToFixInBulk = true,
                Fix = r => SetOption(dataFolder, GameOptions.ModsDisabledKey, false, r)
            };

        bool hasScripts = mods.Any(m => m.Files.Any(f => f.IsEnabled && f.Kind == ModFileKind.Script));
        if (options.ScriptModsEnabled == false && hasScripts)
            yield return new HealthIssue
            {
                Id = "script-mods-disabled-in-game",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Error,
                Title = L.T("Skript-Mods sind im Spiel ausgeschaltet"),
                Description = L.T("„Skript-Mods erlaubt“ ist aus, obwohl Skript-Mods installiert sind – z. B. MC Command Center oder WickedWhims funktionieren so nicht."),
                FixLabel = L.T("Skript-Mods erlauben"),
                IsSafeToFixInBulk = true,
                Fix = r => SetOption(dataFolder, GameOptions.ScriptModsEnabledKey, true, r)
            };

        if (options.ShowModListAtStartup == true)
            yield return new HealthIssue
            {
                Id = "mod-list-at-startup",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Info,
                Title = L.T("CC-Liste beim Spielstart"),
                Description = L.T("Das Spiel zeigt beim Start die Liste aller Mods an. Bei vielen Mods verlängert das den Start spürbar."),
                FixLabel = L.T("Liste ausschalten"),
                IsSafeToFixInBulk = true,
                Fix = r => SetOption(dataFolder, GameOptions.ShowModListKey, false, r)
            };
    }

    private static IEnumerable<HealthIssue> CheckDependencies(IReadOnlyList<ModEntry> mods)
    {
        foreach (var problem in Diagnostics.DependencyChecker.Check(mods))
        {
            string names = string.Join(", ", problem.DependentMods.Take(3).Select(m => m.DisplayName)) + (problem.DependentMods.Count > 3 ? " …" : "");
            bool missing = problem.State == Diagnostics.DependencyState.Missing;
            var library = problem.LibraryMod;
            yield return new HealthIssue
            {
                Id = "dependency-" + problem.Library.ModulePrefix,
                Category = L.T("Abhängigkeiten"),
                Severity = HealthSeverity.Error,
                Title = missing ? L.F("„{0}“ fehlt", problem.Library.Name) : L.F("„{0}“ ist deaktiviert", problem.Library.Name),
                Description = L.F("{0} Mod(s) brauchen {1} und funktionieren ohne nicht ({2}). ", problem.DependentMods.Count, problem.Library.Name, names) +
                              (missing ? L.F("Bitte herunterladen – {0}.", problem.Library.DownloadHint) : L.T("Die Bibliothek ist installiert, aber ausgeschaltet.")),
                Paths = problem.Dependents.Select(f => f.AbsolutePath).ToList(),
                FixLabel = missing ? null : L.T("Bibliothek aktivieren"),
                IsSafeToFixInBulk = !missing,
                Fix = missing || library is null ? null : r =>
                {
                    var result = ModToggleService.SetEnabled(library, true, r);
                    return HealthFixResult.From(result.Success ? 1 : 0, result.Failures.Select(f => f.Message).ToList());
                }
            };
        }
    }

    private static IEnumerable<HealthIssue> CheckScripts(IReadOnlyList<ModEntry> mods, IReadOnlyCollection<string> trusted, Action<string>? trustScript)
    {
        foreach (var result in Diagnostics.ScriptSafetyScanner.Scan(mods))
        {
            string key = TrustKey(result.File);
            if (trusted.Contains(key))
                continue;

            bool dangerous = result.HighestRisk == Diagnostics.ScriptRisk.Dangerous;
            var file = result.File;
            yield return new HealthIssue
            {
                Id = "script-safety-" + key,
                Category = L.T("Sicherheit"),
                Severity = dangerous ? HealthSeverity.Error : HealthSeverity.Info,
                Title = dangerous
                    ? L.F("Möglicher Schadcode in „{0}“", Path.GetFileName(file.AbsolutePath))
                    : L.F("Skript-Mod mit Systemzugriff: „{0}“", Path.GetFileName(file.AbsolutePath)),
                Description = L.F("Der Skript-Mod {0}. ", string.Join(", ", result.Findings.Select(f => f.Description))) +
                              (dangerous
                                  ? L.T("Das ist bei Sims-Mods nicht üblich. Nur behalten, wenn er sicher von einer offiziellen Seite des Erstellers stammt.")
                                  : L.T("Bekannte Mods großer Ersteller tun das manchmal (z. B. für Update-Prüfungen). Wenn der Mod aus einer vertrauenswürdigen Quelle stammt, kann er als vertrauenswürdig markiert werden.")),
                Paths = new[] { file.AbsolutePath },
                FixLabel = dangerous ? L.T("Deaktivieren") : (trustScript is null ? null : L.T("Vertrauen")),
                Fix = dangerous
                    ? r =>
                    {
                        var toggle = ModToggleService.SetFileEnabled(file, false, r);
                        return HealthFixResult.From(toggle.Success ? 1 : 0, toggle.Failures.Select(f => f.Message).ToList());
                    }
                    : trustScript is null ? null : _ =>
                    {
                        trustScript(key);
                        return HealthFixResult.From(1, new List<string>());
                    }
            };
        }
    }

    private static HealthFixResult SetOption(string dataFolder, string key, bool value, Backup.ChangeRecorder recorder)
    {
        if (GameInfo.IsGameRunning())
            return HealthFixResult.From(0, new List<string> { L.T("Sims 4 läuft – bitte das Spiel zuerst beenden, sonst überschreibt es die Einstellung.") });

        // modsdisabled is inverted; the other keys are stored as-is.
        GameOptions.Update(dataFolder, new Dictionary<string, bool> { [key] = value }, recorder);
        return HealthFixResult.From(1, new List<string>());
    }

    private static HealthIssue PatchIssue(PatchInfo patch, IReadOnlyList<AtRiskFile> atRisk) => new()
    {
        Id = "patch-at-risk",
        Category = L.T("Update"),
        Severity = HealthSeverity.Warning,
        Title = L.F("Spiel-Update auf {0} – {1} Mod-Datei(en) gefährdet", GameInfo.ShortVersion(patch.CurrentVersion), atRisk.Count),
        Description = L.F("Das Spiel wurde{0} aktualisiert. ", (patch.PreviousVersion is null ? "" : L.F(" von {0}", GameInfo.ShortVersion(patch.PreviousVersion)))) +
                      L.T("Skript-Mods und Mods, die Spiel-Logik ändern, sind nach Updates oft kaputt, bis ihre Ersteller sie anpassen. ") +
                      L.T("„Sicherer Modus“ deaktiviert sie, bis Updates vorliegen; normaler CC bleibt aktiv."),
        Paths = atRisk.Select(a => a.File.AbsolutePath).ToList(),
        FixLabel = L.T("Sicherer Modus"),
        Fix = r =>
        {
            int count = 0;
            var errors = new List<string>();
            foreach (var file in atRisk.Select(a => a.File))
            {
                var result = ModToggleService.SetFileEnabled(file, false, r);
                if (result.Success) count++;
                else errors.AddRange(result.Failures.Select(f => f.Message));
            }
            return HealthFixResult.From(count, errors);
        }
    };
}
