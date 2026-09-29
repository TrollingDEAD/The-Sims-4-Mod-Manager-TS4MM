using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Export;
using Sims4ModManager.Core.Game;
using Sims4ModManager.Core.Health;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// The "Übersicht" tab: game status (version, updates, in-game mod switches, cache) and every
/// finding of the health check with its fix. Checks run in the background after each rescan.
/// </summary>
public partial class HealthViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsStore _settings = new();
    private HealthReport? _report;
    private string? _gameDataFolder;
    private int _version;
    private bool _loadingOptions;

    public HealthViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;
    }

    public ObservableCollection<HealthIssueViewModel> Issues { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string summary = L.T("Prüfung läuft …");
    [ObservableProperty] private HealthSeverity? overallSeverity;
    [ObservableProperty] private int bulkFixCount;

    /// <summary>Errors + warnings (excludes purely informational findings); drives the tab's badge.</summary>
    [ObservableProperty] private int problemCount;

    // Game card
    [ObservableProperty] private string gameVersionLabel = "–";
    [ObservableProperty] private string patchLabel = string.Empty;
    [ObservableProperty] private bool isNewPatch;
    [ObservableProperty] private int atRiskCount;
    [ObservableProperty] private bool gameRunning;
    [ObservableProperty] private bool hasOptions;
    [ObservableProperty] private bool modsEnabledInGame;
    [ObservableProperty] private bool scriptModsEnabledInGame;
    [ObservableProperty] private bool showModListAtStartup;

    // Cache card
    [ObservableProperty] private string cacheLabel = "–";
    [ObservableProperty] private bool cacheIsStale;

    // Mods card
    [ObservableProperty] private string modsLabel = "–";

    public async Task RefreshAsync()
    {
        int version = ++_version;
        string? modsPath = _main.ModsPath;
        if (modsPath is null || !Directory.Exists(modsPath))
        {
            Issues.Clear();
            Summary = L.T("Kein Mods-Ordner ausgewählt.");
            OverallSeverity = HealthSeverity.Error;
            return;
        }

        var mods = _main.CurrentMods;
        var settings = _settings.Load();
        string? lastSeen = settings.LastSeenGameVersion;
        var trusted = settings.TrustedScripts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        IsBusy = true;
        try
        {
            var report = await Task.Run(() => HealthCheck.Run(modsPath, mods, lastSeen, trusted,
                key => _settings.TryUpdate(s => { if (!s.TrustedScripts.Contains(key)) s.TrustedScripts.Add(key); })));
            if (version != _version)
                return;

            _report = report;
            _gameDataFolder = ModsFolderLocator.TryGetGameDataFolder(modsPath)?.Path;

            // First run: remember the version without raising an "update" alarm.
            if (lastSeen is null && report.GameVersion is not null)
                _settings.TryUpdate(s => s.LastSeenGameVersion = report.GameVersion);

            Show(report, mods);
        }
        finally
        {
            if (version == _version)
                IsBusy = false;
        }
    }

    /// <summary>The checks' findings plus those of the game index (tab "Spiel"), most severe first.</summary>
    private void ShowIssues(HealthReport report)
    {
        var all = report.Issues.Concat(_main.Game.HealthIssues)
            .OrderByDescending(i => i.Severity)
            .ToList();
        Issues.Clear();
        foreach (var issue in all)
            Issues.Add(new HealthIssueViewModel(issue));

        BulkFixCount = all.Count(i => i.CanFix && i.IsSafeToFixInBulk);
        OverallSeverity = all.Count == 0 ? null : all.Max(i => i.Severity);
        int errors = all.Count(i => i.Severity == HealthSeverity.Error);
        int warnings = all.Count(i => i.Severity == HealthSeverity.Warning);
        int infos = all.Count(i => i.Severity == HealthSeverity.Info);
        int fixable = all.Count(i => i.CanFix);
        ProblemCount = errors + warnings;
        Summary = all.Count == 0
            ? L.T("Alles in Ordnung – keine Probleme gefunden.")
            : L.F("{0} Fehler, {1} Warnung(en), {2} Hinweis(e)", errors, warnings, infos) +
              (fixable > 0 ? L.F(" – {0} automatisch behebbar", fixable) : "");
    }

    /// <summary>Called when the game index analysis finished.</summary>
    public void ShowGameIssues()
    {
        if (_report is not null)
            ShowIssues(_report);
    }

    private void Show(HealthReport report, IReadOnlyList<Core.Models.ModEntry> mods)
    {
        ShowIssues(report);

        GameRunning = report.GameRunning;
        GameVersionLabel = report.GameVersion is null ? L.T("unbekannt (Spiel noch nie gestartet?)") : report.GameVersion;
        IsNewPatch = report.Patch?.IsNewSinceLastCheck == true;
        AtRiskCount = report.AtRiskAfterPatch.Count;
        PatchLabel = report.Patch?.UpdatedUtc is { } updated
            ? L.F("Letztes Update: {0:d}", updated.ToLocalTime()) +
              (report.AtRiskAfterPatch.Count > 0 ? L.F(" · {0} Skript-/Gameplay-Mod(s) älter als das Update", report.AtRiskAfterPatch.Count) : L.T(" · keine gefährdeten Mods"))
            : string.Empty;

        _loadingOptions = true;
        HasOptions = report.Options is not null;
        ModsEnabledInGame = report.Options?.ModsEnabled ?? false;
        ScriptModsEnabledInGame = report.Options?.ScriptModsEnabled ?? false;
        ShowModListAtStartup = report.Options?.ShowModListAtStartup ?? false;
        _loadingOptions = false;

        CacheIsStale = report.Cache?.IsStale == true;
        CacheLabel = report.Cache is null
            ? "–"
            : report.Cache.FileCount == 0
                ? "leer"
                : L.F("{0}, zuletzt {1:g}", TrayItemViewModel.FormatSize(report.Cache.SizeBytes), report.Cache.LastWriteUtc?.ToLocalTime()) +
                  (report.Cache.IsStale ? L.T(" – veraltet") : "");

        int enabled = mods.Count(m => m.IsEnabled);
        int scripts = mods.Count(m => m.ContainsScript && (m.IsEnabled || m.IsPartiallyEnabled));
        ModsLabel = L.F("{0} Mods ({1} aktiv, {2} mit Skripten), {3}", mods.Count, enabled, scripts, TrayItemViewModel.FormatSize(mods.Sum(m => m.TotalSizeBytes)));
    }

    // --- In-game switches (Options.ini) ----------------------------------------------------------

    partial void OnModsEnabledInGameChanged(bool value) => ChangeOption(GameOptions.ModsDisabledKey, !value, value ? L.T("Mods im Spiel eingeschaltet") : L.T("Mods im Spiel ausgeschaltet"));
    partial void OnScriptModsEnabledInGameChanged(bool value) => ChangeOption(GameOptions.ScriptModsEnabledKey, value, value ? L.T("Skript-Mods erlaubt") : L.T("Skript-Mods verboten"));
    partial void OnShowModListAtStartupChanged(bool value) => ChangeOption(GameOptions.ShowModListKey, value, value ? L.T("CC-Liste beim Start eingeschaltet") : L.T("CC-Liste beim Start ausgeschaltet"));

    private void ChangeOption(string key, bool value, string description)
    {
        if (_loadingOptions || _gameDataFolder is null)
            return;

        if (GameInfo.IsGameRunning())
        {
            _main.StatusMessage = L.T("Sims 4 läuft – Spieleinstellungen bitte bei geschlossenem Spiel ändern (das Spiel überschreibt sie beim Beenden).");
            _ = RefreshAsync(); // reset the switch
            return;
        }

        try
        {
            using (var recorder = _main.Journal.Begin(description))
                GameOptions.Update(_gameDataFolder, new Dictionary<string, bool> { [key] = value }, recorder);
            _main.StatusMessage = $"{description}. {MainViewModel.UndoHint}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.StatusMessage = L.F("Options.ini konnte nicht geändert werden: {0}", ex.Message);
        }
        _main.History.Refresh(selectNewest: true);
        _ = RefreshAsync();
    }

    // --- Fixes -----------------------------------------------------------------------------------

    [RelayCommand]
    private async Task FixIssueAsync(HealthIssueViewModel? vm)
    {
        if (vm?.Model.Fix is null || !await _main.EnsureGameClosedAsync())
            return;

        var issue = vm.Model;
        bool needsConfirmation = !issue.IsSafeToFixInBulk || issue.Paths.Count > 10;
        if (needsConfirmation && !await _dialogs.ConfirmAsync(issue.Title,
                $"{issue.Description}{Environment.NewLine}{Environment.NewLine}{MainViewModel.UndoHint}", issue.FixLabel ?? L.T("Beheben")))
            return;

        HealthFixResult result;
        using (var recorder = _main.Journal.Begin($"{issue.FixLabel}: {issue.Title}"))
            result = await Task.Run(() => issue.Fix(recorder));

        _main.AfterChange();
        _main.StatusMessage = L.F("{0}: {1} erledigt.", issue.Title, result.Fixed) +
                              (result.Errors.Count > 0 ? L.F(" {0} Problem(e): {1}", result.Errors.Count, string.Join("; ", result.Errors.Take(2))) : "") +
                              $" {MainViewModel.UndoHint}";
    }

    /// <summary>Runs every fix that only tidies up (moves clutter, removes empty folders, enables in-game switches, clears cache).</summary>
    [RelayCommand]
    private async Task FixAllSafeAsync()
    {
        var safe = _report?.Issues.Where(i => i.CanFix && i.IsSafeToFixInBulk).ToList() ?? new List<HealthIssue>();
        if (safe.Count == 0 || !await _main.EnsureGameClosedAsync())
            return;

        string list = string.Join(Environment.NewLine, safe.Select(i => $"• {i.FixLabel}: {i.Title}"));
        if (!await _dialogs.ConfirmAsync(L.T("Automatisch beheben"),
                L.F("Folgendes wird erledigt:{0}{1}{2}{3}", Environment.NewLine, list, Environment.NewLine, Environment.NewLine) +
                L.T("Nichts wird gelöscht: aussortierte Dateien landen in „Mods (aussortiert)“ neben dem Mods-Ordner. ") +
                L.T("Alles lässt sich in einem Schritt über den Verlauf rückgängig machen."), L.T("Beheben")))
            return;

        int done = 0;
        var errors = new List<string>();
        using (var recorder = _main.Journal.Begin(L.F("Übersicht: {0} Problem(e) behoben", safe.Count)))
        {
            foreach (var issue in safe)
            {
                var result = await Task.Run(() => issue.Fix!(recorder));
                done += result.Fixed;
                errors.AddRange(result.Errors);
            }
        }

        _main.AfterChange();
        _main.StatusMessage = L.F("{0} Problem(e) behoben ({1} Änderungen).", safe.Count, done) +
                              (errors.Count > 0 ? L.F(" {0} Fehler: {1}", errors.Count, string.Join("; ", errors.Take(2))) : "") +
                              $" {MainViewModel.UndoHint}";
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (_gameDataFolder is null || !await _main.EnsureGameClosedAsync())
            return;

        int count;
        using (var recorder = _main.Journal.Begin(L.T("Spiel-Cache geleert")))
            count = CacheCleaner.Clear(_gameDataFolder, recorder);
        _main.AfterChange();
        _main.StatusMessage = count == 0 ? L.T("Der Cache war bereits leer.") : L.F("Cache geleert ({0} Datei(en)). Das Spiel baut ihn beim nächsten Start neu auf. {1}", count, MainViewModel.UndoHint);
    }

    /// <summary>Hides the update notice until the next game update.</summary>
    [RelayCommand]
    private async Task AcknowledgePatchAsync()
    {
        if (_report?.GameVersion is { } version)
            _settings.TryUpdate(s => s.LastSeenGameVersion = version);
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenBrokenModsList()
    {
        if (_report?.GameVersion is { } version)
            Process.Start(new ProcessStartInfo(PatchTracker.BrokenModsListUrl(version)) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ShowPaths(HealthIssueViewModel? vm)
    {
        string? path = vm?.Model.Paths.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
        if (path is null)
            return;
        Process.Start("explorer.exe", File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"");
    }

    // --- Play ------------------------------------------------------------------------------------

    public static readonly string NoProfile = L.T("(aktuelle Auswahl)");

    public IEnumerable<string> PlayProfiles => new[] { NoProfile }.Concat(_main.ProfileNames);

    public string PlayProfile
    {
        get => _settings.Load().PlayProfileName ?? NoProfile;
        set
        {
            _settings.TryUpdate(s => s.PlayProfileName = value == NoProfile ? null : value);
            OnPropertyChanged();
        }
    }

    public bool ClearCacheBeforePlay
    {
        get => _settings.Load().ClearCacheBeforePlay;
        set { _settings.TryUpdate(s => s.ClearCacheBeforePlay = value); OnPropertyChanged(); }
    }

    public bool BackupSavesBeforePlay
    {
        get => _settings.Load().BackupSavesBeforePlay;
        set { _settings.TryUpdate(s => s.BackupSavesBeforePlay = value); OnPropertyChanged(); }
    }

    public string InstallLabel => GameLauncher.FindInstallFolder() is { } dir ? L.F("Installation: {0}", dir) : L.T("Installation nicht gefunden");

    [ObservableProperty] private bool isPlaying;

    /// <summary>
    /// "Spielen": optionally apply a profile, clear the cache and back up the saves, start the game,
    /// and after it closes check for new error logs.
    /// </summary>
    [RelayCommand]
    private async Task PlayAsync()
    {
        if (GameInfo.IsGameRunning())
        {
            _main.StatusMessage = L.T("Sims 4 läuft bereits.");
            return;
        }

        var steps = new List<string>();
        string profile = PlayProfile;
        if (profile != NoProfile)
        {
            await _main.ApplyProfileByNameAsync(profile);
            steps.Add(L.F("Profil „{0}“ angewendet", profile));
        }
        if (ClearCacheBeforePlay && _gameDataFolder is not null)
        {
            using (var recorder = _main.Journal.Begin(L.T("Cache vor Spielstart geleert")))
                CacheCleaner.Clear(_gameDataFolder, recorder);
            steps.Add(L.T("Cache geleert"));
        }
        if (BackupSavesBeforePlay || _settings.Load().BackupTrayBeforePlay)
        {
            try
            {
                steps.Add(await _main.Saves.RunAutomaticBackupAsync(_settings.Load().BackupTrayBeforePlay));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                steps.Add(L.F("Sicherung fehlgeschlagen: {0}", ex.Message));
            }
        }

        // State of the Mods folder at game start, for "Was hat sich seit dem letzten Spielen geändert?".
        if (_main.ModsPath is { } modsPath)
        {
            var mods = _main.CurrentMods;
            await Task.Run(() =>
            {
                try { _main.Snapshots.Save(Core.Backup.ModSnapshot.Capture(modsPath, mods, Core.Backup.ModSnapshotStore.ReasonPlay)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); }
            });
        }

        var logsBefore = _gameDataFolder is null ? new HashSet<string>() : Core.Diagnostics.ErrorLogAnalyzer.FindLogFiles(_gameDataFolder, _main.ModsPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? error = GameLauncher.Launch();
        if (error is not null)
        {
            await _dialogs.ShowAsync(L.T("Spiel starten"), error);
            return;
        }

        _main.History.Refresh(selectNewest: true);
        _main.StatusMessage = L.T("Sims 4 wird gestartet") + (steps.Count > 0 ? $" ({string.Join(", ", steps)})" : "") + L.T(". Viel Spaß!");
        IsPlaying = true;
        try
        {
            await GameLauncher.WaitForGameSessionAsync(CancellationToken.None);
        }
        finally
        {
            IsPlaying = false;
        }

        // Back from the game: look for new error logs.
        _main.RescanMods();
        if (_gameDataFolder is not null)
        {
            var newLogs = Core.Diagnostics.ErrorLogAnalyzer.FindLogFiles(_gameDataFolder, _main.ModsPath).Where(f => !logsBefore.Contains(f)).ToList();
            _main.StatusMessage = newLogs.Count == 0
                ? L.T("Spiel beendet – keine neuen Fehler.")
                : L.F("Spiel beendet – {0} neue(s) Fehlerprotokoll(e). Details im Tab „Diagnose“.", newLogs.Count);
        }
    }

    // --- Export ----------------------------------------------------------------------------------

    [RelayCommand]
    private void ExportModList()
    {
        string? path = _dialogs.PickSaveFile(L.T("Mod-Liste exportieren"), "Textdatei (Discord, Foren)|*.txt|CSV-Tabelle (Excel)|*.csv",
            L.F("Sims4-Modliste {0:yyyy-MM-dd}.txt", DateTime.Now));
        if (path is null)
            return;

        var format = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ModListFormat.Csv : ModListFormat.Text;
        try
        {
            ModListExporter.Write(_main.CurrentMods, path, format, _report?.GameVersion);
            _main.StatusMessage = L.F("Mod-Liste exportiert: {0}", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.StatusMessage = L.F("Export fehlgeschlagen: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private void CopyModList()
    {
        Clipboard.SetText(ModListExporter.Render(_main.CurrentMods, ModListFormat.Text, _report?.GameVersion));
        _main.StatusMessage = L.T("Mod-Liste in die Zwischenablage kopiert – z. B. zum Einfügen in Discord oder ein Forum.");
    }
}

public sealed class HealthIssueViewModel
{
    public HealthIssueViewModel(HealthIssue issue) => Model = issue;

    public HealthIssue Model { get; }
    public string Title => Model.Title;
    public string Description => Model.Description;
    public string Category => Model.Category;
    public HealthSeverity Severity => Model.Severity;
    public string? FixLabel => Model.FixLabel;
    public bool CanFix => Model.CanFix;
    public bool HasPaths => Model.Paths.Count > 0;

    /// <summary>First affected paths for the expandable list (the full list can be huge).</summary>
    public IReadOnlyList<string> PathPreview => Model.Paths.Take(50).ToList();
    public string PathsHeader => Model.Paths.Count > 50 ? L.F("Betroffen: {0} (die ersten 50)", Model.Paths.Count) : L.F("Betroffen: {0}", Model.Paths.Count);
}
