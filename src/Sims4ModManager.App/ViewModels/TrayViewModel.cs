using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Tray;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// The "Bibliothek" tab: library items from the Tray folder, the CC each one uses (matched against
/// the mods scanned by <see cref="MainViewModel"/>), download installation, housekeeping, export,
/// deletion with backup and folder backups. Heavy work runs in the background.
/// </summary>
public partial class TrayViewModel : ObservableObject
{
    public static readonly string AllTypes = L.T("Alle");
    public static readonly string OnlyProblems = L.T("Nur Probleme");
    public static readonly string OnlyDisabledCc = L.T("Mit deaktiviertem CC");

    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private int _refreshVersion;

    public TrayViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => o is TrayItemViewModel item && MatchesFilter(item);
    }

    public ObservableCollection<TrayItemViewModel> Items { get; } = new();
    public ICollectionView ItemsView { get; }
    public ObservableCollection<PlacementIssue> Issues { get; } = new();

    public IReadOnlyList<string> TypeFilters { get; } = new[]
    {
        AllTypes, TrayItemViewModel.FormatType(TrayItemType.Household), TrayItemViewModel.FormatType(TrayItemType.Lot),
        TrayItemViewModel.FormatType(TrayItemType.Room), OnlyProblems, OnlyDisabledCc
    };

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string typeFilter = AllTypes;
    [ObservableProperty] private TrayItemViewModel? selectedItem;
    [ObservableProperty] private string? trayPath;
    [ObservableProperty] private string summary = L.T("Bibliothek wird geladen …");
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyText = string.Empty;

    partial void OnSearchTextChanged(string value) => RefreshView();
    partial void OnTypeFilterChanged(string value) => RefreshView();

    /// <summary>
    /// Re-applies the filter. The grid drops the selection when the selected item is filtered out,
    /// which would leave the details panel empty - so select the first visible item instead.
    /// </summary>
    private void RefreshView()
    {
        ItemsView.Refresh();
        if (SelectedItem is null || !MatchesFilter(SelectedItem))
            SelectedItem = ItemsView.Cast<TrayItemViewModel>().FirstOrDefault();
    }

    private bool MatchesFilter(TrayItemViewModel item)
    {
        bool typeOk = TypeFilter == AllTypes ? true
            : TypeFilter == OnlyProblems ? !item.IsComplete
            : TypeFilter == OnlyDisabledCc ? item.DisabledCcCount > 0
            : item.TypeLabel == TypeFilter;
        return typeOk && (string.IsNullOrWhiteSpace(SearchText)
                          || item.SearchText.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>The Tray folder belonging to the current Mods folder ("...\Die Sims 4\Tray").</summary>
    private static string? ResolveTrayPath(string? modsPath)
    {
        if (modsPath is null)
            return null;
        var dataFolder = ModsFolderLocator.TryGetGameDataFolder(modsPath);
        return dataFolder is null ? null : Path.Combine(dataFolder.Path, "Tray");
    }

    /// <summary>Rescans Tray and re-matches CC; called after every mod rescan since CC state may have changed.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        int version = ++_refreshVersion;
        string? modsPath = _main.ModsPath;
        string? trayPath = ResolveTrayPath(modsPath);
        var mods = _main.CurrentMods;
        TrayPath = trayPath;

        if (trayPath is null)
        {
            Items.Clear();
            Issues.Clear();
            Summary = L.T("Kein Tray-Ordner gefunden – der Mods-Ordner liegt nicht in einem Sims 4-Datenordner.");
            _main.ApplyTrayUsage(new Dictionary<ModEntry, int>());
            return;
        }

        IsBusy = true;
        BusyText = L.T("Bibliothek wird analysiert …");
        try
        {
            var (library, cc, issues) = await Task.Run(() =>
            {
                var lib = TrayLibraryScanner.Scan(trayPath);
                var references = new TrayCcAnalyzer(mods).AnalyzeAll(lib.Items);
                var found = TrayHousekeeping.Inspect(trayPath, modsPath);
                return (lib, references, found);
            });

            if (version != _refreshVersion)
                return; // a newer refresh started meanwhile

            ulong? selectedId = SelectedItem?.Model.Id;
            Items.Clear();
            foreach (var item in library.Items)
                Items.Add(new TrayItemViewModel(item, cc[item]));
            SelectedItem = Items.FirstOrDefault(i => i.Model.Id == selectedId) ?? Items.FirstOrDefault();

            Issues.Clear();
            foreach (var issue in issues)
                Issues.Add(issue);

            int households = library.Items.Count(i => i.Type == TrayItemType.Household);
            int lots = library.Items.Count(i => i.Type == TrayItemType.Lot);
            int rooms = library.Items.Count(i => i.Type == TrayItemType.Room);
            int broken = library.Items.Count(i => !i.IsComplete);
            Summary = L.F("{0} Haushalt(e), {1} Grundstück(e), {2} Raum/Räume", households, lots, rooms) +
                      (broken > 0 ? L.F(" – {0} unvollständig", broken) : "") +
                      (issues.Count > 0 ? L.F(" – {0} Datei(en) am falschen Ort", issues.Count) : "");

            var usage = cc.Values
                .SelectMany(refs => refs.Select(r => r.Mod).Distinct())
                .GroupBy(m => m)
                .ToDictionary(g => g.Key, g => g.Count());
            _main.ApplyTrayUsage(usage);
        }
        finally
        {
            if (version == _refreshVersion)
                IsBusy = false;
        }
        await ApplyGameChecksAsync();
    }

    /// <summary>Required packs and missing content per item - needs the game index (tab "Spiel").</summary>
    public async Task ApplyGameChecksAsync()
    {
        var index = _main.Game.Index;
        var items = Items.ToList();
        if (index is null || items.Count == 0)
            return;
        var mods = _main.CurrentMods;
        var checks = await Task.Run(() => new TrayContentChecker(index, mods).CheckAll(items.Select(i => i.Model).ToList()));
        foreach (var item in items)
            if (checks.TryGetValue(item.Model, out var check))
                item.ApplyGameCheck(check);
    }

    // --- Installing downloads ------------------------------------------------------------------

    [RelayCommand]
    private async Task InstallDownloadsAsync()
    {
        var files = _dialogs.PickFiles(L.T("Downloads installieren (Archive, Tray- oder Mod-Dateien)"),
            L.T("Sims 4-Downloads|*.zip;*.rar;*.7z;*.package;*.ts4script;*.trayitem;*.householdbinary;*.blueprint;*.room;*.hhi;*.sgi;*.bpi;*.rmi|Alle Dateien|*.*"));
        if (files.Count > 0)
            await InstallAsync(files);
    }

    [RelayCommand]
    private async Task InstallDownloadFolderAsync()
    {
        string? folder = _dialogs.PickFolder(L.T("Entpackten Download-Ordner installieren"));
        if (folder is not null)
            await InstallAsync(new[] { folder });
    }

    /// <summary>Installs a download archive found lying in Tray or Mods.</summary>
    [RelayCommand]
    private async Task InstallIssueArchiveAsync(PlacementIssue? issue)
    {
        if (issue?.Kind == PlacementIssueKind.ArchiveNotExtracted)
            await InstallAsync(new[] { issue.Path });
    }

    public async Task InstallAsync(IReadOnlyList<string> sources)
    {
        string? modsPath = _main.ModsPath;
        string? trayPath = ResolveTrayPath(modsPath);
        if (modsPath is null || trayPath is null)
        {
            await _dialogs.ShowAsync(L.T("Downloads installieren"), L.T("Es ist kein gültiger Sims 4-Mods-Ordner ausgewählt."));
            return;
        }

        // Installing only ever adds brand-new files - TrayInstaller.Execute skips (never overwrites) a
        // same-named existing file unless overwriteConflicts is passed, which this call site never does -
        // so it cannot touch anything the game may have already loaded. Safe to skip the running-game
        // gate when the user has explicitly opted in, unlike every other mutating operation.
        bool gameRunning = Core.Game.GameInfo.IsGameRunning();
        bool skipGateForLiveInstall = gameRunning && _main.AllowInstallWhileGameRunning;
        if (!skipGateForLiveInstall && !await _main.EnsureGameClosedAsync())
            return;

        IsBusy = true;
        BusyText = L.T("Download wird geprüft …");
        InstallPlan? plan = null;
        try
        {
            plan = await Task.Run(() => TrayInstaller.Analyze(sources, trayPath, modsPath));

            if (plan.NothingToDo)
            {
                await _dialogs.ShowAsync(L.T("Downloads installieren"),
                    L.T("Alles ist bereits installiert – es gibt nichts zu tun.") + FormatWarnings(plan));
                return;
            }

            if (!await _dialogs.ConfirmAsync(L.T("Downloads installieren"), FormatPlan(plan), L.T("Installieren")))
                return;

            BusyText = L.T("Dateien werden kopiert …");
            string what = sources.Count == 1 ? Path.GetFileName(sources[0].TrimEnd('\\', '/')) : L.F("{0} Downloads", sources.Count);
            var result = await Task.Run(() =>
            {
                using var recorder = _main.Journal.Begin(L.F("Installiert: {0}", what));
                return TrayInstaller.Execute(plan, recorder: recorder);
            });
            _main.StatusMessage = L.F("{0} Datei(en) installiert, {1} übersprungen.", result.Installed, result.Skipped) +
                                  (result.Errors.Count > 0 ? L.F(" {0} Fehler: {1}", result.Errors.Count, string.Join("; ", result.Errors.Take(3))) : "") +
                                  (skipGateForLiveInstall ? " " + L.T("Sims 4 läuft noch – der neue Mod erscheint erst nach einem Neustart des Spiels.") : "") +
                                  $" {MainViewModel.UndoHint}";
        }
        finally
        {
            plan?.Dispose();
            IsBusy = false;
        }

        _main.AfterChange(); // also refreshes this tab
    }

    private static string FormatPlan(InstallPlan plan)
    {
        var lines = new List<string>
        {
            L.T("Folgendes wird installiert:"),
            L.F("• Tray (Bibliothek): {0} neue Datei(en)", plan.Count(InstallTargetKind.Tray, InstallStatus.New)),
            L.F("• Mods (CC): {0} neue Datei(en)", plan.Count(InstallTargetKind.Mods, InstallStatus.New))
        };

        int already = plan.Entries.Count(e => e.Status == InstallStatus.AlreadyInstalled && e.Kind != InstallTargetKind.Ignored);
        int conflicts = plan.Entries.Count(e => e.Status == InstallStatus.Conflict);
        int ignored = plan.Entries.Count(e => e.Kind == InstallTargetKind.Ignored);
        if (already > 0) lines.Add(L.F("• Bereits vorhanden (wird übersprungen): {0}", already));
        if (conflicts > 0) lines.Add(L.F("• Andere Datei gleichen Namens vorhanden (wird NICHT überschrieben): {0}", conflicts));
        if (ignored > 0) lines.Add(L.F("• Keine Sims 4-Dateien (ignoriert): {0}", ignored));

        var modTargets = plan.Entries
            .Where(e => e.Kind == InstallTargetKind.Mods && e.Status == InstallStatus.New)
            .Select(e => Path.GetDirectoryName(e.TargetPath))
            .Distinct()
            .Take(3)
            .ToList();
        if (modTargets.Count > 0)
            lines.Add(L.T("CC-Zielordner: ") + string.Join(", ", modTargets));

        return string.Join(Environment.NewLine, lines) + FormatWarnings(plan) + Environment.NewLine + Environment.NewLine + "Fortfahren?";
    }

    private static string FormatWarnings(InstallPlan plan) =>
        plan.Warnings.Count == 0
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + "Hinweise:" + Environment.NewLine +
              string.Join(Environment.NewLine, plan.Warnings.Take(8).Select(w => "• " + w));

    // --- Housekeeping ---------------------------------------------------------------------------

    [RelayCommand]
    private async Task FixIssuesAsync()
    {
        if (!await _main.EnsureGameClosedAsync())
            return;
        var fixable = Issues.Where(i => i.SuggestedTarget is not null).ToList();
        if (fixable.Count == 0)
        {
            await _dialogs.ShowAsync(L.T("Aufräumen"), L.T("Keine automatisch behebbaren Probleme. Nicht entpackte Downloads bitte links auswählen und mit „Archiv installieren“ einrichten."));
            return;
        }

        if (!await _dialogs.ConfirmAsync(L.T("Aufräumen"), L.F("{0} Datei(en) an den richtigen Ort verschieben?", fixable.Count) + Environment.NewLine +
                                            L.T("(Tray-Dateien → Tray, Mod-Dateien → Mods; identische Duplikate werden gesichert.)"), L.T("Verschieben")))
            return;

        var errors = await Task.Run(() =>
        {
            using var recorder = _main.Journal.Begin(L.F("Aufgeräumt: {0} Datei(en) verschoben", fixable.Count));
            return fixable.Select(i => TrayHousekeeping.TryFix(i, recorder)).OfType<string>().ToList();
        });

        _main.StatusMessage = L.F("{0} Datei(en) verschoben.", fixable.Count - errors.Count) +
                              (errors.Count > 0 ? L.F(" {0} Problem(e): {1}", errors.Count, string.Join("; ", errors.Take(3))) : "") +
                              $" {MainViewModel.UndoHint}";
        _main.AfterChange();
    }

    // --- Actions on the selected item --------------------------------------------------------------

    [RelayCommand]
    private void EnableRequiredCc()
    {
        if (SelectedItem is null)
            return;

        var mods = SelectedItem.Cc.Where(r => !r.IsEnabled).Select(r => r.Model.Mod).Distinct().ToList();
        if (mods.Count == 0)
        {
            _main.StatusMessage = L.T("Der gesamte benötigte CC ist bereits aktiv.");
            return;
        }

        string name = SelectedItem.Name;
        List<ToggleFailure> failures;
        using (var recorder = _main.Journal.Begin(L.F("CC für „{0}“ aktiviert", name)))
            failures = mods.SelectMany(m => ModToggleService.SetEnabled(m, enable: true, recorder).Failures).ToList();
        _main.AfterChange();
        _main.StatusMessage = L.F("{0} Mod(s) für „{1}“ aktiviert.", mods.Count, name) +
                              (failures.Count > 0 ? L.F(" {0} Datei(en) konnten nicht umgeschaltet werden (Spiel geöffnet?).", failures.Count) : "") +
                              $" {MainViewModel.UndoHint}";
    }

    [RelayCommand]
    private Task ExportFolderAsync() => ExportAsync(asZip: false);

    [RelayCommand]
    private Task ExportZipAsync() => ExportAsync(asZip: true);

    private async Task ExportAsync(bool asZip)
    {
        var item = SelectedItem;
        if (item is null)
            return;

        string? target = _dialogs.PickFolder(L.F("Zielordner für den Export von „{0}“", item.Name));
        if (target is null)
            return;

        IsBusy = true;
        BusyText = L.T("Export läuft …");
        try
        {
            string path = await Task.Run(() => TrayMaintenance.Export(item.Model, item.Cc.Select(c => c.Model), target, asZip));
            _main.StatusMessage = L.F("„{0}“ mit {1} CC-Datei(en) exportiert: {2}", item.Name, item.Cc.Count, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowAsync(L.T("Export fehlgeschlagen"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var item = SelectedItem;
        if (item is null)
            return;

        if (!await _main.EnsureGameClosedAsync() || !await _dialogs.ConfirmAsync(L.T("Aus Bibliothek entfernen"),
                L.F("„{0}“ ({1}, {2} Dateien) aus der Bibliothek entfernen?", item.Name, item.TypeLabel, item.Model.Files.Count) + Environment.NewLine +
                L.T("Die Dateien werden gesichert und lassen sich über den Verlauf wiederherstellen.") +
                Environment.NewLine + L.T("Der verwendete CC im Mods-Ordner bleibt unverändert."), L.T("Entfernen"), destructive: true))
            return;

        try
        {
            using (var recorder = _main.Journal.Begin(L.F("Aus Bibliothek entfernt: „{0}“", item.Name)))
                TrayMaintenance.DeleteToBackup(item.Model, recorder);
            _main.StatusMessage = L.F("„{0}“ entfernt. {1}", item.Name, MainViewModel.UndoHint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowAsync(L.T("Entfernen fehlgeschlagen"), ex.Message + Environment.NewLine + L.T("(Ist das Spiel geöffnet?)"));
        }

        _main.History.Refresh();
        await RefreshAsync();
    }

    [RelayCommand]
    private void ShowInExplorer()
    {
        string? file = SelectedItem?.Model.Files.FirstOrDefault();
        if (file is not null && File.Exists(file))
            Process.Start("explorer.exe", $"/select,\"{file}\"");
        else if (TrayPath is not null && Directory.Exists(TrayPath))
            Process.Start("explorer.exe", $"\"{TrayPath}\"");
    }

    // --- Backup -------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        string? modsPath = _main.ModsPath;
        var dataFolder = modsPath is null ? null : ModsFolderLocator.TryGetGameDataFolder(modsPath);
        if (dataFolder is null)
        {
            await _dialogs.ShowAsync(L.T("Sicherung"), L.T("Kein Sims 4-Datenordner gefunden."));
            return;
        }

        string? zipPath = _dialogs.PickSaveFile(L.T("Sicherung speichern"), "ZIP-Archiv|*.zip",
            L.F("Sims4-Sicherung {0:yyyy-MM-dd}.zip", DateTime.Now));
        if (zipPath is null)
            return;

        long modsSize = _main.CurrentMods.Sum(m => m.TotalSizeBytes);
        bool includeMods = await _dialogs.ConfirmAsync(L.T("Sicherung"),
            L.F("Tray (Bibliothek) und Saves (Spielstände) werden gesichert.{0}{1}", Environment.NewLine, Environment.NewLine) +
            L.F("Zusätzlich den Mods-Ordner sichern ({0})?", TrayItemViewModel.FormatSize(modsSize)), L.T("Mit Mods"), L.T("Ohne Mods"));

        IsBusy = true;
        BusyText = L.T("Sicherung wird erstellt …");
        try
        {
            var progress = new Progress<string>(file => BusyText = L.F("Sicherung: {0}", file));
            var skipped = await Task.Run(() =>
                TrayMaintenance.CreateBackup(dataFolder.Path, zipPath, includeTray: true, includeSaves: true, includeMods, progress));
            _main.StatusMessage = L.F("Sicherung erstellt: {0}", zipPath) +
                                  (skipped.Count > 0 ? L.F(" ({0} gesperrte Datei(en) übersprungen – Spiel geöffnet?)", skipped.Count) : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowAsync(L.T("Sicherung fehlgeschlagen"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
