using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Conflicts;
using Sims4ModManager.Core.Export;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Updates;

namespace Sims4ModManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppSettingsStore _settingsStore = new();
    private readonly ProfileStore _profileStore = new();
    private readonly ModNotesStore _notes = new();
    private readonly ModSnapshotStore _snapshots = new();

    /// <summary>Per-mod notes/tags/reason/group store, for the "why is this here" export.</summary>
    public ModNotesStore Notes => _notes;

    private IReadOnlyList<ModEntry> _currentMods = Array.Empty<ModEntry>();

    public ObservableCollection<ModEntryViewModel> Mods { get; } = new();
    public ObservableCollection<ConflictGroupViewModel> ConflictGroups { get; } = new();
    public ObservableCollection<string> ScanWarnings { get; } = new();
    public ObservableCollection<string> ProfileNames { get; } = new();

    /// <summary>Recently used and auto-detected Mods folders offered in the folder drop-down.</summary>
    public ObservableCollection<string> KnownModsPaths { get; } = new();

    private bool _suppressSelectionHandlers;

    [ObservableProperty]
    private string? modsPath;

    /// <summary>Drop-down selection; picking an entry switches to that folder.</summary>
    [ObservableProperty]
    private string? selectedKnownModsPath;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string? selectedProfileName;

    [ObservableProperty]
    private string newProfileName = string.Empty;

    [ObservableProperty]
    private string conflictSummary = L.F("Konflikte: {0}", 0);

    [ObservableProperty]
    private ConflictGroupViewModel? selectedConflictGroup;

    [ObservableProperty]
    private bool hasConflicts;

    // --- Navigation ------------------------------------------------------------------------------

    public const int TabOverview = 0, TabMods = 1, TabCatalog = 2, TabLibrary = 3, TabDiagnose = 4, TabSaves = 5, TabStorage = 6, TabHistory = 7, TabGame = 8, TabUpdates = 9;

    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == TabHistory)
            _ = History.RefreshChangesAsync();
    }

    /// <summary>Selects a mod in the "Mods" tab (from the catalog, the storage view or the search).</summary>
    public void ShowMod(ModEntry mod)
    {
        var vm = Mods.FirstOrDefault(m => m.Model == mod) ?? Mods.FirstOrDefault(m => m.Id == mod.Id);
        if (vm is null)
            return;
        if (!ModsView.Cast<ModEntryViewModel>().Contains(vm))
        {
            ModSearchText = string.Empty;
            ModFilter = FilterAll;
        }
        SelectedTabIndex = TabMods;
        SelectedMod = vm;
        ModScrollRequested?.Invoke(vm);
    }

    /// <summary>Asks the view to scroll the mod list to an entry.</summary>
    public event Action<ModEntryViewModel>? ModScrollRequested;

    /// <summary>
    /// Asks the view to commit any in-progress DataGrid cell edit right away. Raised before a mod
    /// toggle touches the Mods collection: the checkbox click that triggered the toggle leaves the
    /// grid's underlying CollectionView with an open edit transaction, and rebuilding/refreshing
    /// that collection while the transaction is still open throws ("'Refresh' ist während einer
    /// AddNew- oder EditItem-Transaktion nicht zulässig") - reliably reproducible by toggling a
    /// second mod before the first one's rescan has applied.
    /// </summary>
    public event Action? ModEditCommitNeeded;

    // --- Mod list filtering -------------------------------------------------------------------

    public static readonly string FilterAll = L.T("Alle");
    public static readonly string FilterEnabled = L.T("Aktiv");
    public static readonly string FilterDisabled = L.T("Deaktiviert");
    public static readonly string FilterPartial = L.T("Teilweise aktiv");
    public static readonly string FilterConflicts = L.T("Mit Konflikten");
    public static readonly string FilterUsedInLibrary = L.T("In Bibliothek verwendet");
    public static readonly string FilterWithNotes = L.T("Mit Notizen");
    public static readonly string FilterReplacesGame = L.T("Ersetzt Spielinhalte");
    public static readonly string FilterFavorites = L.T("Favoriten");

    public IReadOnlyList<string> ModFilters { get; } = new[]
    {
        FilterAll, FilterEnabled, FilterDisabled, FilterPartial, FilterConflicts, FilterUsedInLibrary, FilterWithNotes, FilterReplacesGame, FilterFavorites
    };

    /// <summary>Filtered view of <see cref="Mods"/> shown in the grid.</summary>
    public ICollectionView ModsView { get; }

    [ObservableProperty]
    private string modSearchText = string.Empty;

    [ObservableProperty]
    private string modFilter = FilterAll;

    /// <summary>"123 von 2266 Mods" - shown next to the filter.</summary>
    [ObservableProperty]
    private string modCountLabel = string.Empty;

    partial void OnModSearchTextChanged(string value) => RefreshModsView();
    partial void OnModFilterChanged(string value) => RefreshModsView();

    private void RefreshModsView()
    {
        ModsView.Refresh();
        int visible = ModsView.Cast<object>().Count();
        ModCountLabel = visible == Mods.Count ? L.F("{0} Mods", Mods.Count) : L.F("{0} von {1} Mods", visible, Mods.Count);
    }

    private bool MatchesModFilter(ModEntryViewModel mod)
    {
        bool statusOk = true;
        if (ModFilter == FilterEnabled) statusOk = mod.Model.IsEnabled;
        else if (ModFilter == FilterDisabled) statusOk = !mod.Model.IsEnabled && !mod.IsPartiallyEnabled;
        else if (ModFilter == FilterPartial) statusOk = mod.IsPartiallyEnabled;
        else if (ModFilter == FilterConflicts) statusOk = mod.HasConflict;
        else if (ModFilter == FilterUsedInLibrary) statusOk = mod.TrayUsage > 0;
        else if (ModFilter == FilterWithNotes) statusOk = mod.HasNote;
        else if (ModFilter == FilterReplacesGame) statusOk = mod.ReplacesGame;
        else if (ModFilter == FilterFavorites) statusOk = mod.IsFavorite;
        if (!statusOk)
            return false;

        string search = ModSearchText.Trim();
        return search.Length == 0
               || mod.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || mod.CreatorLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || mod.CategoryLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || mod.Tags.Any(t => t.Contains(search, StringComparison.CurrentCultureIgnoreCase))
               || (mod.NoteTooltip?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    [RelayCommand]
    private void ToggleFavorite(ModEntryViewModel? mod)
    {
        if (mod is null)
            return;
        mod.IsFavorite = _notes.ToggleFavorite(mod.Id);
        if (ModFilter == FilterFavorites)
            RefreshModsView();
    }

    // --- Bulk tagging: apply/remove a tag across the grid's current multi-selection, instead of -----
    // opening each mod's details panel one at a time. Fed from the DataGrid's SelectionChanged event ---
    // in the code-behind (WPF's DataGrid has no two-way SelectedItems binding). --------------------------

    public ObservableCollection<ModEntryViewModel> SelectedMods { get; } = new();

    /// <summary>Shows the bulk-tag bar once more than one row is selected.</summary>
    public bool HasMultiSelection => SelectedMods.Count > 1;

    public string BulkSelectionLabel => L.F("{0} Mods ausgewählt:", SelectedMods.Count);

    [ObservableProperty]
    private string bulkTagText = string.Empty;

    /// <summary>Called by the view whenever the mod grid's selection changes.</summary>
    public void SetSelectedMods(IEnumerable<ModEntryViewModel> mods)
    {
        SelectedMods.Clear();
        foreach (var mod in mods)
            SelectedMods.Add(mod);
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(BulkSelectionLabel));
    }

    [RelayCommand]
    private void ApplyBulkTag()
    {
        string tag = BulkTagText.Trim();
        if (tag.Length == 0 || SelectedMods.Count == 0)
            return;

        foreach (var mod in SelectedMods.ToList())
        {
            var note = _notes.Get(mod.Id) ?? new ModNote();
            if (!note.Tags.Any(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)))
                note.Tags.Add(tag);
            _notes.Set(mod.Id, note);
            mod.SetNote(note);
        }
        RefreshSelectedModDetails();
        StatusMessage = L.F("Tag „{0}“ zu {1} Mod(s) hinzugefügt.", tag, SelectedMods.Count);
        if (ModFilter == FilterWithNotes)
            RefreshModsView();
    }

    [RelayCommand]
    private void RemoveBulkTag()
    {
        string tag = BulkTagText.Trim();
        if (tag.Length == 0 || SelectedMods.Count == 0)
            return;

        int removed = 0;
        foreach (var mod in SelectedMods.ToList())
        {
            var note = _notes.Get(mod.Id);
            if (note is null || note.Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)) == 0)
                continue;
            _notes.Set(mod.Id, note);
            mod.SetNote(note);
            removed++;
        }
        RefreshSelectedModDetails();
        StatusMessage = removed > 0
            ? L.F("Tag „{0}“ von {1} Mod(s) entfernt.", tag, removed)
            : L.F("Kein ausgewählter Mod hatte den Tag „{0}“.", tag);
        if (ModFilter == FilterWithNotes)
            RefreshModsView();
    }

    [ObservableProperty]
    private string bulkGroupText = string.Empty;

    /// <summary>Puts every selected mod into the same "always enable together" group, replacing any group they had.</summary>
    [RelayCommand]
    private void SetBulkGroup()
    {
        string group = BulkGroupText.Trim();
        if (group.Length == 0 || SelectedMods.Count == 0)
            return;

        foreach (var mod in SelectedMods.ToList())
        {
            var note = _notes.Get(mod.Id) ?? new ModNote();
            note.Group = group;
            _notes.Set(mod.Id, note);
            mod.SetNote(note);
        }
        RefreshSelectedModDetails();
        StatusMessage = L.F("{0} Mod(s) zur Gruppe „{1}“ hinzugefügt.", SelectedMods.Count, group);
    }

    /// <summary>Takes every selected mod out of its "always enable together" group, if any.</summary>
    [RelayCommand]
    private void ClearBulkGroup()
    {
        if (SelectedMods.Count == 0)
            return;

        int cleared = 0;
        foreach (var mod in SelectedMods.ToList())
        {
            var note = _notes.Get(mod.Id);
            if (note?.Group is null)
                continue;
            note.Group = null;
            _notes.Set(mod.Id, note);
            mod.SetNote(note);
            cleared++;
        }
        RefreshSelectedModDetails();
        StatusMessage = cleared > 0
            ? L.F("{0} Mod(s) aus ihrer Gruppe entfernt.", cleared)
            : L.T("Kein ausgewählter Mod war Teil einer Gruppe.");
    }

    /// <summary>Re-reads the open details panel's note fields after a bulk edit, in case it's part of the selection.</summary>
    private void RefreshSelectedModDetails()
    {
        if (SelectedMod is { } mod && SelectedMods.Contains(mod))
            ModDetails = new ModDetailsViewModel(this, _notes, mod);
    }

    // --- Selected mod (details panel) ---------------------------------------------------------------

    [ObservableProperty]
    private ModEntryViewModel? selectedMod;

    [ObservableProperty]
    private ModDetailsViewModel? modDetails;

    /// <summary>0 = "Details", 1 = "Konflikte" on the right of the mod list.</summary>
    [ObservableProperty]
    private int modSideTabIndex;

    partial void OnSelectedModChanged(ModEntryViewModel? value)
    {
        if (ModDetails?.IsDirty == true)
            ModDetails.SaveNotesCommand.Execute(null); // never lose typed notes by clicking elsewhere
        ModDetails = value is null ? null : new ModDetailsViewModel(this, _notes, value);
    }

    // --- Theme & language --------------------------------------------------------------------------

    /// <summary>Dark (default) or light theme; persisted in the settings.</summary>
    [ObservableProperty]
    private bool isDarkTheme = true;

    private bool _loadingTheme;

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeService.Apply(value, AccentColor);
        if (!_loadingTheme)
            _settingsStore.TryUpdate(s => s.Theme = value ? "Dark" : "Light");
    }

    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    /// <summary>Accent color override, "#RRGGBB"; null means "follow the Windows accent color" (the default).</summary>
    [ObservableProperty]
    private string? accentColor;

    [ObservableProperty]
    private bool isAccentMenuOpen;

    [RelayCommand]
    private void ToggleAccentMenu() => IsAccentMenuOpen = !IsAccentMenuOpen;

    public IReadOnlyList<string> AccentPresets => ThemeService.AccentPresets;

    partial void OnAccentColorChanged(string? value)
    {
        ThemeService.Apply(IsDarkTheme, value);
        if (!_loadingTheme)
            _settingsStore.TryUpdate(s => s.AccentColor = value);
    }

    [RelayCommand]
    private void SetAccentColor(string? hex)
    {
        AccentColor = string.IsNullOrEmpty(hex) ? null : hex;
        IsAccentMenuOpen = false;
    }

    // --- Toast notifications: brief, dismissible pop-ups for background events (a silent update check --------
    // finding something, a scheduled backup running) that would otherwise only show up as status bar text
    // easily overwritten by the next status update before anyone reads it. -------------------------------------

    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <summary>Last <see cref="ToastHistoryLimit"/> toasts, newest first, kept after they auto-dismiss.</summary>
    public ObservableCollection<ToastViewModel> ToastHistory { get; } = new();

    private const int ToastHistoryLimit = 30;

    [ObservableProperty]
    private bool isToastHistoryOpen;

    /// <summary>True once a toast has arrived that the history popup hasn't been opened to see yet.</summary>
    [ObservableProperty]
    private bool hasUnseenToasts;

    [RelayCommand]
    private void ToggleToastHistory()
    {
        IsToastHistoryOpen = !IsToastHistoryOpen;
        if (IsToastHistoryOpen)
            HasUnseenToasts = false;
    }

    public void ShowToast(string message, ToastKind kind = ToastKind.Info)
    {
        var toast = new ToastViewModel(message, kind);
        Toasts.Add(toast);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();

        ToastHistory.Insert(0, toast);
        while (ToastHistory.Count > ToastHistoryLimit)
            ToastHistory.RemoveAt(ToastHistory.Count - 1);
        if (!IsToastHistoryOpen)
            HasUnseenToasts = true;
    }

    [RelayCommand]
    private void DismissToast(ToastViewModel? toast)
    {
        if (toast is not null)
            Toasts.Remove(toast);
    }

    [RelayCommand]
    private Task ShowShortcutsAsync() => _dialogs.ShowAsync(L.T("Tastenkürzel"), L.T(
        "F5 – Mods neu einlesen\n" +
        "Strg+Z – Letzte Aktion rückgängig machen\n" +
        "Strg+F – Mods durchsuchen (im Mods-Tab)\n" +
        "Strg+K – Überall suchen (Mods, Bibliothek, Spielstände)\n" +
        "Esc – aktives Suchfeld leeren\n" +
        "Enter – bestes Ergebnis der Überall-Suche öffnen"));

    // --- Portable settings backup (export/import for moving to a new PC) ---------------------------

    [ObservableProperty]
    private bool isBackupMenuOpen;

    [RelayCommand]
    private void ToggleBackupMenu() => IsBackupMenuOpen = !IsBackupMenuOpen;

    private const string BackupFileFilter = "Sims4ModManager-Sicherung (*.s4mmbackup.json)|*.s4mmbackup.json|Alle Dateien (*.*)|*.*";

    [RelayCommand]
    private void ExportBackup()
    {
        IsBackupMenuOpen = false;
        string? path = _dialogs.PickSaveFile(L.T("Sicherung exportieren"), L.T(BackupFileFilter),
            $"Sims4ModManager-Sicherung-{DateTime.Now:yyyy-MM-dd}.s4mmbackup.json");
        if (path is null)
            return;
        try
        {
            var backup = PortableBackupService.Capture(_settingsStore, _notes, _profileStore);
            PortableBackupService.SaveToFile(backup, path);
            StatusMessage = L.F("Sicherung gespeichert: {0}", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = L.F("Sicherung konnte nicht gespeichert werden: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        IsBackupMenuOpen = false;
        string? path = _dialogs.PickFiles(L.T("Sicherung importieren"), L.T(BackupFileFilter)).FirstOrDefault();
        if (path is null)
            return;

        var backup = PortableBackupService.TryLoadFromFile(path);
        if (backup is null)
        {
            StatusMessage = L.T("Diese Datei ist keine gültige Sims4ModManager-Sicherung.");
            return;
        }

        bool confirmed = await _dialogs.ConfirmAsync(
            L.T("Sicherung importieren"),
            L.F("Einstellungen, {0} Notiz(en)/Favorit(en) und {1} Profil(e) werden übernommen (bestehende Profile mit " +
                "gleichem Namen werden ersetzt). Die App startet danach neu.", backup.Notes.Count, backup.Profiles.Count),
            L.T("Importieren & neu starten"), L.T("Abbrechen"));
        if (!confirmed)
            return;

        PortableBackupService.Apply(backup, _settingsStore, _notes, _profileStore);
        if (Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>Label of the language button: the language it switches to.</summary>
    public string OtherLanguageLabel => L.IsGerman ? "EN" : "DE";

    /// <summary>App version shown in the title bar (from the assembly version, e.g. "v1.0.1").</summary>
    public string AppVersion { get; } =
        "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

    [RelayCommand]
    private async Task ToggleLanguageAsync()
    {
        string target = L.IsGerman ? L.English : L.German;
        bool restart = await _dialogs.ConfirmAsync(
            L.IsGerman ? L.T("Switch to English") : L.T("Auf Deutsch umstellen"),
            L.IsGerman
                ? L.T("The app restarts in English. Your mods, profiles and settings stay as they are.\n\n(Die App startet auf Englisch neu.)")
                : L.T("Die App startet auf Deutsch neu. Mods, Profile und Einstellungen bleiben unverändert.\n\n(The app restarts in German.)"),
            L.IsGerman ? L.T("Restart now") : L.T("Jetzt neu starten"),
            L.IsGerman ? "Cancel" : L.T("Abbrechen"));
        if (!restart)
            return;
        _settingsStore.TryUpdate(s => s.Language = target);
        if (Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    private readonly IDialogService _dialogs;
    public IDialogService Dialogs => _dialogs;

    /// <summary>Backup journal: every action that changes files is recorded here and can be undone.</summary>
    public ChangeJournal Journal { get; } = new();

    public ModSnapshotStore Snapshots => _snapshots;
    public AppSettingsStore Settings => _settingsStore;

    /// <summary>The "Verlauf" tab.</summary>
    public HistoryViewModel History { get; }

    /// <summary>The "Übersicht" tab (health check).</summary>
    public HealthViewModel Health { get; }

    /// <summary>The "Diagnose" tab (error logs, 50/50 test).</summary>
    public DiagnoseViewModel Diagnose { get; }

    /// <summary>The "Spielstände" tab.</summary>
    public SavesViewModel Saves { get; }

    /// <summary>The "Katalog" tab; also provides categories and creators to the other tabs.</summary>
    public CatalogViewModel Catalog { get; }

    /// <summary>The "Speicherplatz" tab.</summary>
    public StorageViewModel Storage { get; }

    /// <summary>Search box in the title bar.</summary>
    public SearchViewModel Search { get; }

    /// <summary>Tab "Spiel": game index, default replacements, recolors without mesh.</summary>
    public GameViewModel Game { get; }

    /// <summary>Tab "CurseForge" › "Updates": checks installed CurseForge mods for newer releases.</summary>
    public UpdatesViewModel Updates { get; }

    /// <summary>Tab "CurseForge" › "Browse": search/install from the Sims 4 catalog.</summary>
    public BrowseViewModel Browse { get; }

    /// <summary>App self-update (title bar): checks GitHub Releases, separate from the CurseForge mod updates above.</summary>
    public AppUpdateViewModel AppUpdate { get; }

    // --- Conflict resolution ----------------------------------------------------------------------

    private IReadOnlyList<ResolutionProposal> _allProposals = Array.Empty<ResolutionProposal>();

    /// <summary>Proposals for the selected conflict group.</summary>
    public ObservableCollection<ResolutionProposalViewModel> GroupProposals { get; } = new();

    [ObservableProperty]
    private int safeProposalCount;

    partial void OnSelectedConflictGroupChanged(ConflictGroupViewModel? value)
    {
        GroupProposals.Clear();
        if (value is null)
            return;
        foreach (var proposal in ConflictResolver.ForGroup(_allProposals, value.Model)
                     .OrderByDescending(p => p.IsRecommended))
            GroupProposals.Add(new ResolutionProposalViewModel(proposal));
    }

    [RelayCommand]
    private async Task ApplyProposalAsync(ResolutionProposalViewModel? vm)
    {
        if (vm is null)
            return;
        var proposal = vm.Model;

        string question = proposal.Action == ResolutionAction.DisableFile
            ? $"{vm.Explanation}{Environment.NewLine}{Environment.NewLine}" +
              L.F("„{0}“ wird deaktiviert (umbenannt, nicht gelöscht).", ConflictResolver.Label(proposal.Change))
            : $"{vm.Explanation}{Environment.NewLine}{Environment.NewLine}" +
              L.T("Das Package wird dafür neu geschrieben. Die Originaldatei wird vorher gesichert.");
        if (!await EnsureGameClosedAsync() || !await _dialogs.ConfirmAsync(vm.Title, question + Environment.NewLine + UndoHint, L.T("Anwenden")))
            return;

        ResolutionResult result;
        using (var recorder = Journal.Begin(L.F("Konflikt gelöst: {0}", vm.ActionLabel)))
            result = ConflictResolver.Apply(proposal, recorder);

        AfterChange();
        StatusMessage = result.Success ? $"{L.T(result.Message)} {UndoHint}" : L.F("Fehler: {0}", L.T(result.Message));
    }

    /// <summary>Applies every safe proposal (disable duplicates, older versions, NonHQ variants, merged-set leftovers) in one undoable step.</summary>
    [RelayCommand]
    private async Task ApplySafeProposalsAsync()
    {
        var safe = ConflictResolver.SafeProposals(_lastReport);
        if (safe.Count == 0)
        {
            await _dialogs.ShowAsync(L.T("Sichere Lösungen"), L.T("Es gibt keine Konflikte, die sich gefahrlos automatisch lösen lassen."));
            return;
        }

        var byKind = safe.GroupBy(p => p.Kind).Select(g => $"• {ResolutionProposalViewModel.FormatKind(g.Key)}: {g.Count()}");
        string message = L.F("{0} Datei(en) werden deaktiviert (umbenannt, nicht gelöscht) – jeweils nur, wenn ihr Inhalt an anderer Stelle vollständig oder in besserer Version vorhanden ist:", safe.Count) +
                         Environment.NewLine + string.Join(Environment.NewLine, byKind) + Environment.NewLine + Environment.NewLine +
                         L.T("Überschreibungen zwischen verschiedenen Mods werden nicht angetastet – die bitte einzeln prüfen.") +
                         Environment.NewLine + L.T("Alles lässt sich in einem Schritt über den Verlauf rückgängig machen.");
        if (!await EnsureGameClosedAsync() || !await _dialogs.ConfirmAsync(L.T("Sichere Lösungen anwenden"), message, L.T("Anwenden")))
            return;

        int ok = 0;
        var errors = new List<string>();
        using (var recorder = Journal.Begin(L.F("{0} Konflikte automatisch gelöst", safe.Count)))
        {
            foreach (var proposal in safe)
            {
                var result = ConflictResolver.Apply(proposal, recorder);
                if (result.Success) ok++;
                else errors.Add(L.T(result.Message));
            }
        }

        AfterChange();
        StatusMessage = L.F("{0} Datei(en) deaktiviert.", ok) +
                        (errors.Count > 0 ? " " + L.F("{0} Fehler: {1}", errors.Count, string.Join("; ", errors.Take(2))) : "") + $" {UndoHint}";
    }

    /// <summary>Exports every conflict group, its affected resources and the resolver's recommended fix as a shareable document.</summary>
    [RelayCommand]
    private void ExportConflictReport()
    {
        if (_lastReport.Groups.Count == 0)
        {
            StatusMessage = L.T("Keine Konflikte zwischen aktiven Mods.");
            return;
        }

        string? path = _dialogs.PickSaveFile(L.T("Konfliktbericht exportieren"), "Textdatei|*.txt|CSV-Tabelle (Excel)|*.csv",
            L.F("Sims4-Konfliktbericht {0:yyyy-MM-dd}.txt", DateTime.Now));
        if (path is null)
            return;

        var format = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ModListFormat.Csv : ModListFormat.Text;
        try
        {
            ConflictReportExporter.Write(_lastReport, _allProposals, path, format);
            StatusMessage = L.F("Konfliktbericht exportiert: {0}", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = L.F("Export fehlgeschlagen: {0}", ex.Message);
        }
    }

    private ConflictReport _lastReport = ConflictReport.Empty;

    /// <summary>The "Bibliothek" tab; refreshed after every mod rescan.</summary>
    public TrayViewModel Tray { get; }

    /// <summary>Mods from the last scan (enabled and disabled), shared with the library tab.</summary>
    public IReadOnlyList<ModEntry> CurrentMods => _currentMods;

    public MainViewModel() : this(new WpfDialogService())
    {
    }

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        History = new HistoryViewModel(this, dialogs);
        Health = new HealthViewModel(this, dialogs);
        Diagnose = new DiagnoseViewModel(this, dialogs);
        Saves = new SavesViewModel(this, dialogs);
        Catalog = new CatalogViewModel(this);
        Catalog.Updated += OnCatalogUpdated;
        Storage = new StorageViewModel(this);
        Search = new SearchViewModel(this);
        Game = new GameViewModel(this, dialogs);
        Updates = new UpdatesViewModel(this, dialogs);
        Browse = new BrowseViewModel(this, dialogs);
        AppUpdate = new AppUpdateViewModel(this);
        StartDownloadWatcher();
        Tray = new TrayViewModel(this, dialogs);
        Game.IndexReady += () => _ = Tray.ApplyGameChecksAsync();
        History.Refresh();
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = o => o is ModEntryViewModel mod && MatchesModFilter(mod);

        var resolution = _settingsStore.ResolveModsPath();
        var settings = _settingsStore.Load();

        _loadingTheme = true;
        AccentColor = settings.AccentColor;
        IsDarkTheme = !string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        _loadingTheme = false;

        RefreshProfiles();
        _suppressSelectionHandlers = true;
        if (settings.LastProfileName is not null && ProfileNames.Contains(settings.LastProfileName, StringComparer.OrdinalIgnoreCase))
            SelectedProfileName = ProfileNames.First(n => string.Equals(n, settings.LastProfileName, StringComparison.OrdinalIgnoreCase));
        _suppressSelectionHandlers = false;

        ShowModsPath(resolution.Path);
        if (resolution.Path is not null)
        {
            var startupScan = RescanModsForStartupAsync(resolution.Notice is null ? null : L.T(resolution.Notice));
            _ = RunUpdateCheckAfterAsync(startupScan);
        }
        else if (resolution.Notice is not null)
        {
            StatusMessage = L.T(resolution.Notice);
        }

        _ = Game.RefreshAsync(); // loads (or builds) the game index in the background
        _ = AppUpdate.CheckAsync();
        _ = ShowWhatsNewIfNeededAsync(settings);
    }

    /// <summary>
    /// Waits for the startup mod scan before running the background CurseForge update check - starting
    /// it any earlier would see an empty mod list and burn its rate-limited check window for nothing.
    /// </summary>
    private async Task RunUpdateCheckAfterAsync(Task scanTask)
    {
        await scanTask;
        await Updates.CheckIfDueAsync();
    }

    /// <summary>
    /// Shows the current version's changelog section once, the first time it runs after an update.
    /// Silent on the very first install (nothing to compare against) and when the version has no
    /// changelog section (e.g. a locally built dev version).
    /// </summary>
    private async Task ShowWhatsNewIfNeededAsync(AppSettings settings)
    {
        // Runs from the constructor before MainWindow.Show() - yield first so the dialog gets a
        // proper owner and doesn't appear ahead of (and block) the main window becoming visible.
        await Task.Yield();

        string current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        if (string.Equals(settings.LastSeenAppVersion, current, StringComparison.OrdinalIgnoreCase))
            return;

        _settingsStore.TryUpdate(s => s.LastSeenAppVersion = current);
        if (settings.LastSeenAppVersion is null)
            return; // first install: the setup assistant covers this instead

        string? section = ChangelogReader.SectionFor(current);
        if (section is null)
            return;
        await _dialogs.ShowAsync(L.F("Neu in Version {0}", current), ChangelogReader.ToPlainText(section));
    }

    /// <summary>Marks the mods that replace game content (icon + filter in the mod list).</summary>
    public void ApplyGameReplacements(IReadOnlyList<Core.Game.GameReplacement> replacements)
    {
        var byMod = replacements.GroupBy(r => r.Mod).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var mod in Mods)
        {
            mod.GameReplacementTooltip = byMod.TryGetValue(mod.Model, out var list)
                ? L.T("Ersetzt Spielinhalte: ") + string.Join("; ", list.Select(r => r.Summary).Distinct().Take(3))
                : null;
        }
        if (ModFilter == FilterReplacesGame)
            RefreshModsView();
    }

    /// <summary>First start: the window shows the setup assistant once it is loaded.</summary>
    public bool ShouldShowSetup => !_settingsStore.Load().SetupCompleted;

    private void OnCatalogUpdated()
    {
        foreach (var mod in Mods)
        {
            mod.CategoryLabel = Catalog.CategoryLabelOf(mod.Model);
            mod.CreatorLabel = Catalog.CreatorOf(mod.Model) ?? string.Empty;
        }
        if (SelectedMod is not null && ModDetails is not null && !ModDetails.IsDirty)
            ModDetails = new ModDetailsViewModel(this, _notes, SelectedMod);
        if (ModSearchText.Length > 0)
            RefreshModsView();
        _ = Storage.RefreshAsync();
        _ = Game.AnalyzeModsAsync(); // recolor check needs the catalog's mesh references
    }

    /// <summary>Sets the active folder and refreshes the drop-down (recent + detected folders) without triggering a switch.</summary>
    private void ShowModsPath(string? path)
    {
        ModsPath = path;

        var settings = _settingsStore.Load();
        var detected = ModsFolderLocator.FindGameDataFolders()
            .Where(f => f.ModsFolderExists)
            .Select(f => f.ModsPath);
        var known = (path is null ? Enumerable.Empty<string>() : new[] { path })
            .Concat(settings.RecentModsPaths)
            .Concat(detected)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _suppressSelectionHandlers = true;
        KnownModsPaths.Clear();
        foreach (var p in known)
            KnownModsPaths.Add(p);
        SelectedKnownModsPath = path;
        _suppressSelectionHandlers = false;
    }

    partial void OnSelectedKnownModsPathChanged(string? value)
    {
        if (_suppressSelectionHandlers || value is null
            || string.Equals(value, ModsPath, StringComparison.OrdinalIgnoreCase))
            return;

        SetModsPath(value);
    }

    partial void OnSelectedProfileNameChanged(string? value)
    {
        if (!_suppressSelectionHandlers)
            _settingsStore.TryUpdate(s => s.LastProfileName = value);
    }

    [RelayCommand]
    private void DetectModsFolder()
    {
        var folders = ModsFolderLocator.FindGameDataFolders();
        var best = folders.FirstOrDefault(f => f.ModsFolderExists);
        if (best is null)
        {
            var withoutMods = folders.FirstOrDefault();
            StatusMessage = withoutMods is null
                ? L.T("Kein Sims 4-Datenordner gefunden. Bitte den Mods-Ordner manuell auswählen.")
                : L.F("Sims 4-Datenordner gefunden ({0}), aber noch ohne Mods-Ordner. Das Spiel einmal starten oder den Ordner „Mods“ dort anlegen.", withoutMods.Path);
            return;
        }

        SetModsPath(best.ModsPath);
        int others = folders.Count(f => f.ModsFolderExists) - 1;
        StatusMessage = L.F("Mods-Ordner erkannt: {0}", best.ModsPath) +
                        (others > 0 ? " " + L.F("({0} weitere Installation(en) in der Auswahlliste)", others) : "") +
                        $" – {StatusMessage}";
    }

    public WindowPlacement? LoadWindowPlacement() => _settingsStore.Load().Window;

    public void SaveWindowPlacement(WindowPlacement placement) =>
        _settingsStore.TryUpdate(s => s.Window = placement);

    /// <summary>Shows in the mod list how many library items use each mod (from the library tab's CC analysis).</summary>
    public void ApplyTrayUsage(IReadOnlyDictionary<ModEntry, int> usage)
    {
        foreach (var mod in Mods)
            mod.TrayUsage = usage.GetValueOrDefault(mod.Model);
        if (ModFilter == FilterUsedInLibrary)
            RefreshModsView();
    }

    [RelayCommand]
    private void Rescan() => RescanMods();

    /// <summary>Rescans the Mods folder, then re-analyzes the library in the background (CC state may have changed).</summary>
    public void RescanMods()
    {
        RescanCore();
        _ = Catalog.RefreshAsync();
        _ = Tray.RefreshAsync();
        _ = Health.RefreshAsync();
        _ = Diagnose.RefreshAsync();
        _ = Saves.RefreshAsync();
        TakeDailySnapshot();
    }

    /// <summary>Once a day the state of the Mods folder is recorded for "Was hat sich geändert?".</summary>
    private void TakeDailySnapshot()
    {
        string? path = ModsPath;
        var mods = _currentMods;
        if (path is null || mods.Count == 0)
            return;
        _ = Task.Run(() =>
        {
            try { _snapshots.EnsureDaily(path, mods); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); }
        });
    }

    private void RescanCore()
    {
        string? selectedId = SelectedMod?.Id;
        if (ModsPath is null || !Directory.Exists(ModsPath))
        {
            StatusMessage = L.T("Mods-Ordner existiert nicht.");
            _currentMods = Array.Empty<ModEntry>();
            Mods.Clear();
            ShowConflicts(ConflictReport.Empty);
            return;
        }

        var scanned = ModScanner.Scan(ModsPath);
        var report = ConflictDetector.FindConflicts(scanned);
        ApplyScanResult(scanned, report, selectedId);
    }

    /// <summary>
    /// Scans the Mods folder and detects conflicts on a background thread, then applies the result on
    /// the UI thread - used only for the very first scan at startup, so the splash screen's spinner
    /// keeps animating instead of freezing for the scan's duration (WPF stops compositing new frames
    /// for a window whose UI thread has stopped pumping messages; see SplashWindow.xaml). Every other
    /// caller uses the synchronous RescanMods()/RescanCore(), where blocking briefly during an explicit
    /// user action (clicking "Rescan", undoing a change, ...) is an acceptable, well-understood tradeoff
    /// that isn't worth the added complexity of threading through every call site.
    /// </summary>
    private async Task RescanModsForStartupAsync(string? notice)
    {
        string? path = ModsPath;
        if (path is null || !Directory.Exists(path))
        {
            RescanCore(); // trivial/instant in this branch - no need for the background-thread path
        }
        else
        {
            var (scanned, report) = await Task.Run(() =>
            {
                var mods = ModScanner.Scan(path);
                return (mods, ConflictDetector.FindConflicts(mods));
            });
            ApplyScanResult(scanned, report, selectedId: null);
        }

        if (notice is not null)
            StatusMessage = $"{notice} – {StatusMessage}";

        // Same follow-up as RescanMods(): re-analyze the library etc., since CC state may have changed.
        // Note: Game.RefreshAsync() (started independently, right after this call) may finish first and
        // fire IndexReady before this scan completes, running Tray's game-content check against a still-
        // empty mod list; it self-corrects here once RefreshAsync() below re-runs it.
        _ = Catalog.RefreshAsync();
        _ = Tray.RefreshAsync();
        _ = Health.RefreshAsync();
        _ = Diagnose.RefreshAsync();
        _ = Saves.RefreshAsync();
        TakeDailySnapshot();
    }

    /// <summary>
    /// Async counterpart to RescanMods() (keeps the current selection, unlike the startup variant):
    /// scans and detects conflicts on a background thread before rebuilding the Mods collection.
    /// Used by <see cref="OnModToggleRequested"/> so a single checkbox click doesn't run the full,
    /// synchronous scan+conflict-detection while the DataGridCheckBoxColumn that raised the click is
    /// still mid-toggle: rebuilding (Mods.Clear()/Add) the bound collection from inside that same
    /// input dispatch corrupted the grid's edit state, which is what made re-enabling a mod right
    /// after disabling it appear to silently do nothing, on top of the scan blocking the UI thread
    /// for large mod lists.
    /// </summary>
    private async Task RescanModsAsync()
    {
        string? selectedId = SelectedMod?.Id;
        string? path = ModsPath;
        if (path is null || !Directory.Exists(path))
        {
            RescanCore();
        }
        else
        {
            var (scanned, report) = await Task.Run(() =>
            {
                var mods = ModScanner.Scan(path);
                return (mods, ConflictDetector.FindConflicts(mods));
            });
            ApplyScanResult(scanned, report, selectedId);
        }

        _ = Catalog.RefreshAsync();
        _ = Tray.RefreshAsync();
        _ = Health.RefreshAsync();
        _ = Diagnose.RefreshAsync();
        _ = Saves.RefreshAsync();
        TakeDailySnapshot();
    }

    private void ApplyScanResult(IReadOnlyList<ModEntry> scanned, ConflictReport report, string? selectedId)
    {
        _currentMods = scanned;

        // Updates rows in place (matched by Id) instead of Mods.Clear() + re-adding everything: a
        // Clear() is a collection Reset, which makes the DataGrid throw away and regenerate every row
        // container - including the checkbox column's per-cell "current cell" state. Since a rescan
        // runs after every single toggle, that reset every row's interaction state on every click,
        // and the checkbox column then needed a spare "warm-up" click before the next one actually
        // registered - see ModEntryViewModel.UpdateModel. Only mods that were actually added, removed
        // or reordered touch the collection itself; everything else is just a property refresh.
        var existingById = Mods.ToDictionary(m => m.Id);
        var scannedIds = scanned.Select(m => m.Id).ToHashSet();
        for (int i = Mods.Count - 1; i >= 0; i--)
        {
            if (!scannedIds.Contains(Mods[i].Id))
                Mods.RemoveAt(i);
        }

        for (int i = 0; i < scanned.Count; i++)
        {
            var mod = scanned[i];
            ModEntryViewModel vm;
            if (existingById.TryGetValue(mod.Id, out var existing))
            {
                vm = existing;
                vm.UpdateModel(mod);
                int currentIndex = Mods.IndexOf(vm);
                if (currentIndex != i)
                    Mods.Move(currentIndex, i);
            }
            else
            {
                vm = new ModEntryViewModel(mod, OnModToggleRequested);
                Mods.Insert(i, vm);
            }

            vm.SetNote(_notes.Get(mod.Id));
            vm.IsBroken = mod.Files.Any(f => f.IsUnreadable);
            vm.HasUpdateAvailable = Updates.ModIdsWithUpdates.Contains(mod.Id);
            if (Catalog.HasData)
            {
                vm.CategoryLabel = Catalog.CategoryLabelOf(mod);
                vm.CreatorLabel = Catalog.CreatorOf(mod) ?? string.Empty;
            }
        }

        ShowConflicts(report);
        RefreshModsView(); // after ShowConflicts: the "Mit Konflikten" filter needs the conflict flags
        SelectedMod = selectedId is null ? null : Mods.FirstOrDefault(m => m.Id == selectedId);

        // Mod files placed next to the Mods folder instead of inside it are silently ignored by the game.
        var misplaced = ModsPath is null ? Array.Empty<string>()
            : ModsFolderLocator.TryGetGameDataFolder(ModsPath)?.MisplacedModFiles ?? Array.Empty<string>();
        foreach (var file in misplaced)
            ScanWarnings.Add(L.F("{0} liegt außerhalb des Mods-Ordners ({1}) und wird vom Spiel nicht geladen.", Path.GetFileName(file), Path.GetDirectoryName(file)));

        int enabledCount = _currentMods.Count(m => m.IsEnabled);
        StatusMessage = L.F("{0} Mods gefunden, {1} aktiv, {2} Konfliktgruppe(n) mit {3} Konflikt(en).",
                            _currentMods.Count, enabledCount, report.Groups.Count, report.TotalItemCount) +
                        (report.UnreadableFiles.Count > 0 ? " " + L.F("{0} Datei(en) nicht lesbar.", report.UnreadableFiles.Count) : "") +
                        (misplaced.Count > 0 ? " " + L.F("{0} Mod-Datei(en) außerhalb des Mods-Ordners.", misplaced.Count) : "");
    }

    private void ShowConflicts(ConflictReport report)
    {
        // Proposals first: selecting a group below shows its proposals.
        _lastReport = report;
        _allProposals = ConflictResolver.ProposeAll(report);
        SafeProposalCount = ConflictResolver.SafeProposals(report).Count;

        ConflictGroups.Clear();
        foreach (var group in report.Groups)
            ConflictGroups.Add(new ConflictGroupViewModel(group));
        SelectedConflictGroup = ConflictGroups.FirstOrDefault();
        HasConflicts = report.Groups.Count > 0;

        // Mark conflicting mods in the list (warning icon + "Mit Konflikten" filter).
        var conflictsByMod = report.Groups
            .SelectMany(g => g.Mods.Select(m => (Mod: m, Group: g)))
            .GroupBy(x => x.Mod)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Group).ToList());
        foreach (var mod in Mods)
        {
            mod.ConflictTooltip = conflictsByMod.TryGetValue(mod.Model, out var groups)
                ? L.F("Konflikte mit: {0} ({1} Ressourcen) – Details rechts unter „Konflikte“.",
                    string.Join(", ", groups.SelectMany(g => g.Mods).Where(m => m != mod.Model).Select(m => m.DisplayName).Distinct().Take(5)),
                    groups.Sum(g => g.Items.Count))
                : null;
        }

        int highCount = report.Groups.Count(g => g.Severity == ConflictSeverity.High);
        ConflictSummary = L.F("Konflikte: {0} Gruppe(n), davon {1} schwer", report.Groups.Count, highCount);
        if (report.IgnoredIdenticalCount > 0)
            ConflictSummary += " " + L.F("({0} identische Duplikate ignoriert)", report.IgnoredIdenticalCount);

        ScanWarnings.Clear();
        foreach (var unreadable in report.UnreadableFiles)
        {
            string what = unreadable.File.Kind == ModFileKind.Script ? L.T("Skript-Archiv") : L.T("Package");
            ScanWarnings.Add(L.F("{0}: {1} – {2} nicht lesbar, nicht auf Konflikte geprüft (beschädigt oder gesperrt?)",
                unreadable.Mod.DisplayName, unreadable.File.RelativePathInMod, what));
        }
    }

    /// <summary>Switches to <paramref name="path"/> (e.g. from the folder dialog), remembers it and rescans.</summary>
    public void SetModsPath(string path)
    {
        string normalized = ModsFolderLocator.NormalizeSelectedFolder(path);
        bool corrected = !string.Equals(normalized, ModsFolderLocator.NormalizePath(path), StringComparison.OrdinalIgnoreCase);

        bool saved = _settingsStore.TryRememberModsPath(normalized);
        ShowModsPath(normalized);
        RescanMods();

        if (corrected)
            StatusMessage = L.T("Sims 4-Datenordner gewählt – verwende den darin liegenden Mods-Ordner.") + " " + StatusMessage;
        if (!saved)
            StatusMessage += " " + L.T("Achtung: Einstellungen konnten nicht gespeichert werden.");
    }

    [RelayCommand]
    private Task EnableAllAsync() => ToggleAllAsync(enable: true);

    [RelayCommand]
    private Task DisableAllAsync() => ToggleAllAsync(enable: false);

    private async Task ToggleAllAsync(bool enable)
    {
        if (!await EnsureGameClosedAsync())
            return;

        var failures = new List<ToggleFailure>();
        string description = enable ? L.T("Alle Mods aktiviert") : L.T("Alle Mods deaktiviert");
        using (var recorder = Journal.Begin(description))
        {
            foreach (var mod in _currentMods)
            {
                if (mod.IsEnabled == enable)
                    continue;

                var result = ModToggleService.SetEnabled(mod, enable, recorder);
                if (!result.Success)
                    failures.AddRange(result.Failures);
            }
        }

        AfterChange();
        StatusMessage = failures.Count > 0
            ? L.F("{0} Datei(en) konnten nicht umgeschaltet werden (evtl. Spiel geöffnet).", failures.Count)
            : $"{description}. {UndoHint}";
    }

    private async void OnModToggleRequested(ModEntryViewModel vm, bool enable)
    {
        ModEditCommitNeeded?.Invoke();

        // "Always enable together" group: other mods sharing vm's group name that aren't already in
        // the target state toggle along with it, as one journaled action.
        string? group = _notes.Get(vm.Id)?.Group;
        var mates = group is null
            ? Array.Empty<ModEntry>()
            : _currentMods.Where(m => !string.Equals(m.Id, vm.Id, StringComparison.OrdinalIgnoreCase)
                                       && string.Equals(_notes.Get(m.Id)?.Group, group, StringComparison.CurrentCultureIgnoreCase)
                                       && (m.IsEnabled != enable || m.IsPartiallyEnabled))
                .ToArray();

        // Disabling CC that a save uses makes Sims lose hair/clothes/furniture there - ask first,
        // for vm and every grouped mate that would be disabled along with it.
        var affected = mates.Length == 0 ? new[] { vm.Model } : new[] { vm.Model }.Concat(mates).ToArray();
        var savesUsing = enable ? new List<string>() : affected.SelectMany(Saves.SavesUsing).Distinct().ToList();
        if (savesUsing.Count > 0 && !await _dialogs.ConfirmAsync(L.T("Wird in Spielständen verwendet"),
                L.F("„{0}“ wird in {1} verwendet. Ohne diesen CC fehlen dort Kleidung, Haare oder Objekte (beim Speichern werden sie durch Standardinhalte ersetzt).",
                    vm.DisplayName, string.Join(", ", savesUsing)) + "\n\n" + L.T("Trotzdem deaktivieren?"),
                L.T("Deaktivieren"), destructive: true))
        {
            RescanMods(); // puts the checkbox back
            return;
        }

        string description = mates.Length == 0
            ? (enable ? L.F("„{0}“ aktiviert", vm.DisplayName) : L.F("„{0}“ deaktiviert", vm.DisplayName))
            : (enable ? L.F("„{0}“ und {1} gruppierte Mod(s) aktiviert", vm.DisplayName, mates.Length)
                      : L.F("„{0}“ und {1} gruppierte Mod(s) deaktiviert", vm.DisplayName, mates.Length));

        var failures = new List<ToggleFailure>();
        using (var recorder = Journal.Begin(description))
        {
            var result = ModToggleService.SetEnabled(vm.Model, enable, recorder);
            if (!result.Success)
                failures.AddRange(result.Failures);
            foreach (var mate in mates)
            {
                var mateResult = ModToggleService.SetEnabled(mate, enable, recorder);
                if (!mateResult.Success)
                    failures.AddRange(mateResult.Failures);
            }
        }

        await RescanModsAsync();
        History.Refresh(selectNewest: true);
        if (failures.Count > 0)
            StatusMessage = L.F("Fehler: {0}", string.Join("; ", failures.Select(f => $"{Path.GetFileName(f.FilePath)}: {f.Message}")));
        else if (mates.Length > 0)
            StatusMessage = description;
    }

    /// <summary>
    /// Asks before changing files while Sims 4 runs: renames fail on files the game has open, and
    /// changes only take effect after a restart anyway. Returns true to continue.
    /// </summary>
    public async Task<bool> EnsureGameClosedAsync()
    {
        if (!Core.Game.GameInfo.IsGameRunning())
            return true;
        return await _dialogs.ConfirmAsync(L.T("Sims 4 läuft"),
            L.T("Sims 4 ist gerade geöffnet. Dateien, die das Spiel benutzt, lassen sich dann nicht ändern, und alle Änderungen wirken erst nach einem Neustart des Spiels.") +
            Environment.NewLine + Environment.NewLine + L.T("Am besten zuerst das Spiel beenden. Trotzdem fortfahren?"), L.T("Trotzdem fortfahren"));
    }

    // --- Sorting (collections) -----------------------------------------------------------------------

    [RelayCommand]
    private void OpenSort()
    {
        if (ModsPath is null)
            return;
        if (!Catalog.HasData)
        {
            StatusMessage = L.T("Der Katalog wird noch aufgebaut – bitte gleich noch einmal versuchen.");
            return;
        }
        var window = new SortWindow(new SortViewModel(this)) { Owner = System.Windows.Application.Current.MainWindow };
        window.ShowDialog();
    }

    /// <summary>Turns a folder mod into a collection: each file/subfolder inside becomes its own mod.</summary>
    public async Task MakeCollectionAsync(ModEntry mod)
    {
        if (!mod.IsFolder || mod.ContainsScript)
            return;
        if (!await _dialogs.ConfirmAsync(L.T("Als Sammelordner behandeln"),
                L.F("„{0}“ wird zum Sammelordner: jede Datei und jeder Unterordner darin erscheint danach als eigener Mod und lässt sich einzeln schalten. Dateien werden nicht verschoben – es wird nur eine Markierungsdatei angelegt.", mod.DisplayName),
                L.T("Umwandeln")))
            return;

        string marker = Path.Combine(mod.AbsolutePath, ModScanner.CollectionMarker);
        string source = Path.Combine(Path.GetTempPath(), "s4mm-sammlung.txt");
        File.WriteAllText(source, L.T("Sammelordner des Sims 4 Mod Managers: jede Datei und jeder Unterordner hier ist ein eigener Mod."));
        using (var recorder = Journal.Begin(L.F("„{0}“ als Sammelordner markiert", mod.DisplayName)))
            recorder.CopyIn(source, marker);
        File.Delete(source);
        AfterChange();
        StatusMessage = L.F("„{0}“ ist jetzt ein Sammelordner.", mod.DisplayName) + " " + UndoHint;
    }

    /// <summary>
    /// Re-applies the "update available" row icon from the Updates tab's last check without a full
    /// rescan - called right after that check finishes, since it doesn't itself touch any mod files.
    /// </summary>
    public void RefreshModUpdateFlags()
    {
        foreach (var mod in Mods)
            mod.HasUpdateAvailable = Updates.ModIdsWithUpdates.Contains(mod.Id);
    }

    /// <summary>Every collection folder currently in use, for the "move to collection" picker.</summary>
    public IReadOnlyList<string> KnownCollections => _currentMods
        .Select(m => m.Collection).Where(c => c.Length > 0)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// Moves a mod into a different collection folder (or, with an empty target, directly into the
    /// Mods folder) instead of dragging it in Explorer. The mod's ID is just its file/folder name -
    /// see <see cref="Sims4ModManager.Core.ModScanner"/> - so notes, tags and profile membership
    /// keep pointing at it afterward without anything to update.
    /// </summary>
    public async Task MoveModToCollectionAsync(ModEntry mod, string targetCollection)
    {
        if (ModsPath is null || !await EnsureGameClosedAsync())
            return;

        string target = targetCollection.Trim().Trim('\\', '/');
        string targetLabel = target.Length == 0 ? L.T("den Mods-Ordner") : L.F("den Sammelordner „{0}“", target);

        ModMoveResult result;
        using (var recorder = Journal.Begin(L.F("„{0}“ in {1} verschoben", mod.DisplayName, targetLabel)))
            result = ModMover.Move(mod, ModsPath, target, recorder);

        if (!result.Success)
        {
            StatusMessage = result.Reason ?? L.T("Verschieben nicht möglich.");
            return;
        }

        AfterChange();
        StatusMessage = L.F("„{0}“ in {1} verschoben.", mod.DisplayName, targetLabel) + " " + UndoHint;
    }

    // --- Download watcher --------------------------------------------------------------------------

    private DownloadWatcher? _downloadWatcher;

    /// <summary>A finished Sims 4 download waiting for "Installieren" (shown as a banner).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingDownloadName), nameof(HasPendingDownload))]
    private string? pendingDownload;

    public string PendingDownloadName => PendingDownload is null ? string.Empty : Path.GetFileName(PendingDownload);
    public bool HasPendingDownload => PendingDownload is not null;

    private void StartDownloadWatcher()
    {
        _downloadWatcher = new DownloadWatcher();
        _downloadWatcher.DownloadDetected += path =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => PendingDownload = path);
        _downloadWatcher.Enabled = _settingsStore.Load().WatchDownloads;
    }

    public bool WatchDownloads
    {
        get => _downloadWatcher?.Enabled ?? false;
        set
        {
            if (_downloadWatcher is not null)
                _downloadWatcher.Enabled = value;
            _settingsStore.TryUpdate(s => s.WatchDownloads = value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Opt-in: skip the "Sims 4 is running" confirmation for installing downloads specifically, since
    /// that operation only ever adds brand-new files and never touches one the game may have loaded.
    /// </summary>
    public bool AllowInstallWhileGameRunning
    {
        get => _settingsStore.Load().AllowInstallWhileGameRunning;
        set { _settingsStore.TryUpdate(s => s.AllowInstallWhileGameRunning = value); OnPropertyChanged(); }
    }

    [RelayCommand]
    private async Task InstallPendingDownloadAsync()
    {
        string? path = PendingDownload;
        PendingDownload = null;
        if (path is not null && File.Exists(path))
            await Tray.InstallAsync(new[] { path });
    }

    [RelayCommand]
    private void DismissPendingDownload() => PendingDownload = null;

    /// <summary>Appended to status messages after changes: every change can be undone.</summary>
    public static string UndoHint => L.T("Rückgängig: Tab „Verlauf“ oder Strg+Z.");

    /// <summary>Rescans the mods and refreshes the history after an action changed files.</summary>
    public void AfterChange()
    {
        RescanMods();
        History.Refresh(selectNewest: true);
    }

    /// <summary>Reloads the profile list, keeping the current selection (clearing the list would otherwise reset it).</summary>
    private void RefreshProfiles(string? select = null)
    {
        string? keep = select ?? SelectedProfileName;

        bool wasSuppressed = _suppressSelectionHandlers;
        _suppressSelectionHandlers = true;
        ProfileNames.Clear();
        foreach (var name in _profileStore.ListProfileNames())
            ProfileNames.Add(name);
        SelectedProfileName = ProfileNames.FirstOrDefault(n => string.Equals(n, keep, StringComparison.OrdinalIgnoreCase));
        _suppressSelectionHandlers = wasSuppressed;
    }

    [RelayCommand]
    private void SaveProfile()
    {
        string name = NewProfileName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusMessage = L.T("Bitte einen Profilnamen eingeben.");
            return;
        }

        bool overwriting = ProfileNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        var profile = ProfileStore.CaptureCurrent(name, _currentMods, ModsPath);
        try
        {
            _profileStore.Save(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = L.F("Profil „{0}“ konnte nicht gespeichert werden: {1}", name, ex.Message);
            return;
        }

        RefreshProfiles(select: name);
        _settingsStore.TryUpdate(s => s.LastProfileName = name);
        NewProfileName = string.Empty;
        StatusMessage = overwriting
            ? L.F("Profil „{0}“ aktualisiert ({1} aktive Mods).", name, profile.EnabledModIds.Count)
            : L.F("Profil „{0}“ gespeichert ({1} aktive Mods).", name, profile.EnabledModIds.Count);
    }

    [RelayCommand]
    /// <summary>Applies a profile by name (used by "Spielen").</summary>
    public Task ApplyProfileByNameAsync(string name)
    {
        SelectedProfileName = ProfileNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        return ApplySelectedProfileAsync();
    }

    [RelayCommand]
    private async Task ApplySelectedProfileAsync()
    {
        if (SelectedProfileName is null)
        {
            StatusMessage = L.T("Kein Profil ausgewählt.");
            return;
        }

        var profile = _profileStore.Load(SelectedProfileName);
        if (profile is null)
        {
            StatusMessage = L.T("Profil konnte nicht geladen werden.");
            return;
        }

        if (!await EnsureGameClosedAsync())
            return;

        ProfileApplyResult result;
        using (var recorder = Journal.Begin(L.F("Profil „{0}“ angewendet", profile.Name)))
            result = ProfileStore.Apply(profile, _currentMods, recorder);
        AfterChange();

        var notes = new List<string>();
        if (result.Failures.Count > 0)
            notes.Add(L.F("{0} Datei(en) konnten nicht umgeschaltet werden (evtl. Spiel geöffnet)", result.Failures.Count));
        if (result.MissingModIds.Count > 0)
            notes.Add(L.F("{0} Mod(s) aus dem Profil sind nicht mehr vorhanden ({1})", result.MissingModIds.Count,
                string.Join(", ", result.MissingModIds.Take(3)) + (result.MissingModIds.Count > 3 ? ", …" : "")));
        if (profile.ModsPath is not null && ModsPath is not null
            && !string.Equals(ModsFolderLocator.NormalizePath(profile.ModsPath), ModsFolderLocator.NormalizePath(ModsPath), StringComparison.OrdinalIgnoreCase))
            notes.Add(L.F("Profil wurde für einen anderen Mods-Ordner erstellt ({0})", profile.ModsPath));

        StatusMessage = notes.Count == 0
            ? L.F("Profil „{0}“ angewendet.", profile.Name) + " " + UndoHint
            : L.F("Profil „{0}“ angewendet.", profile.Name) + " " + L.F("Hinweis: {0}.", string.Join("; ", notes));
    }

    [RelayCommand]
    private void DeleteSelectedProfile()
    {
        if (SelectedProfileName is null)
            return;

        string name = SelectedProfileName;
        try
        {
            _profileStore.Delete(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = L.F("Profil „{0}“ konnte nicht gelöscht werden: {1}", name, ex.Message);
            return;
        }

        SelectedProfileName = null; // also clears the remembered last profile
        RefreshProfiles();
        StatusMessage = L.F("Profil „{0}“ gelöscht.", name);
    }
}
