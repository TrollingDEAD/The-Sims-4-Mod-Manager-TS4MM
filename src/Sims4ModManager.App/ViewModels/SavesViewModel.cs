using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Saves;
using Sims4ModManager.Core.Tray;

namespace Sims4ModManager.App.ViewModels;

/// <summary>The "Spielstände" tab: saves with the CC they use, backups (manual and automatic) and restore.</summary>
public partial class SavesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private readonly SaveBackups _backups = new();
    private readonly TrayMirror _trayMirror = new();
    private int _version;

    /// <summary>Mod file path → labels of saves using it (for the warning before disabling).</summary>
    private ConcurrentDictionary<string, List<string>> _usage = new(StringComparer.OrdinalIgnoreCase);

    public SavesViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;
    }

    public ObservableCollection<SaveViewModel> Saves { get; } = new();
    public ObservableCollection<SaveBackupViewModel> Backups { get; } = new();

    [ObservableProperty] private SaveViewModel? selectedSave;
    [ObservableProperty] private string summary = L.T("Spielstände werden gelesen …");
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string autoBackupStatus = string.Empty;

    private string? GameDataFolder => _main.ModsPath is { } mods ? ModsFolderLocator.TryGetGameDataFolder(mods)?.Path : null;

    public async Task RefreshAsync()
    {
        int version = ++_version;
        string? data = GameDataFolder;
        if (data is null)
            return;

        var mods = _main.CurrentMods;
        IsBusy = true;
        try
        {
            var saves = await Task.Run(() => SaveGameReader.List(data));
            if (version != _version)
                return;

            int? selectedSlot = SelectedSave?.Model.Slot;
            Saves.Clear();
            foreach (var save in saves)
                Saves.Add(new SaveViewModel(save));
            SelectedSave = Saves.FirstOrDefault(s => s.Model.Slot == selectedSlot) ?? Saves.FirstOrDefault();
            RefreshBackups();

            Summary = saves.Count == 0 ? L.T("Keine Spielstände gefunden.") : L.F("{0} Spielstand/-stände – CC-Nutzung wird ermittelt …", saves.Count);

            // CC usage per save in the background (about a second per save).
            var analyzer = await Task.Run(() => new TrayCcAnalyzer(mods));
            var usage = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var vm in Saves.ToList())
            {
                var cc = await Task.Run(() => SaveGameReader.FindUsedCc(vm.Model.Path, analyzer));
                if (version != _version)
                    return;
                vm.SetCc(cc);
                foreach (var reference in cc)
                    usage.GetOrAdd(reference.File.AbsolutePath, _ => new List<string>()).Add(vm.Label);
            }
            _usage = usage;
            Summary = L.F("{0} Spielstand/-stände, zusammen {1} verwendete CC-Datei(en).", saves.Count, usage.Count);
        }
        finally
        {
            if (version == _version)
                IsBusy = false;
        }
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var backup in _backups.List())
            Backups.Add(new SaveBackupViewModel(backup));
        var last = _main.Settings.Load().LastAutoBackupUtc;
        AutoBackupStatus = last is null
            ? L.T("Noch keine automatische Sicherung.")
            : L.F("Letzte automatische Sicherung: {0}", last.Value.ToLocalTime().ToString("g", L.Culture)) +
              (_trayMirror.Exists ? " · " + L.T("Bibliothek gespiegelt") : "");
    }

    /// <summary>Saves that use CC from this mod (empty while the usage is still being computed).</summary>
    public IReadOnlyList<string> SavesUsing(ModEntry mod) =>
        mod.Files.SelectMany(f => _usage.TryGetValue(f.AbsolutePath, out var saves) ? saves : new List<string>())
            .Distinct().ToList();

    [RelayCommand]
    private async Task BackupSelectedAsync()
    {
        if (SelectedSave is null)
            return;
        var save = SelectedSave.Model;
        try
        {
            var backup = await Task.Run(() => _backups.Create(save));
            Backups.Insert(0, new SaveBackupViewModel(backup));
            _main.StatusMessage = L.F("{0} gesichert: {1}", SelectedSave.Label, backup.Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.StatusMessage = L.F("Sicherung fehlgeschlagen: {0}", ex.Message);
        }
    }

    // --- Automatic backups ---------------------------------------------------------------------------

    public static readonly string IntervalNever = L.T("Nie");
    public static readonly string IntervalDaily = L.T("Täglich");
    public static readonly string IntervalWeekly = L.T("Wöchentlich");

    public IReadOnlyList<string> IntervalOptions { get; } = new[] { IntervalNever, IntervalDaily, IntervalWeekly };
    public IReadOnlyList<int> KeepOptions { get; } = new[] { 3, 5, 10, 20 };

    public bool BackupSavesBeforePlay
    {
        get => _main.Settings.Load().BackupSavesBeforePlay;
        set { _main.Settings.TryUpdate(s => s.BackupSavesBeforePlay = value); OnPropertyChanged(); }
    }

    public bool BackupTrayBeforePlay
    {
        get => _main.Settings.Load().BackupTrayBeforePlay;
        set { _main.Settings.TryUpdate(s => s.BackupTrayBeforePlay = value); OnPropertyChanged(); }
    }

    public string Interval
    {
        get => _main.Settings.Load().AutoBackupIntervalDays switch { <= 0 => IntervalNever, 1 => IntervalDaily, _ => IntervalWeekly };
        set
        {
            int days = value == IntervalDaily ? 1 : value == IntervalWeekly ? 7 : 0;
            _main.Settings.TryUpdate(s => s.AutoBackupIntervalDays = days);
            OnPropertyChanged();
        }
    }

    public int KeepPerSlot
    {
        get => _main.Settings.Load().KeepAutoBackupsPerSlot;
        set { _main.Settings.TryUpdate(s => s.KeepAutoBackupsPerSlot = value); OnPropertyChanged(); }
    }

    public string TrayMirrorFolder => _trayMirror.Root;

    /// <summary>
    /// Automatic backup (before playing or on schedule): every save slot (main file only) plus, if
    /// enabled, the incremental Tray mirror; old automatic backups beyond the limit are deleted.
    /// </summary>
    public async Task<string> RunAutomaticBackupAsync(bool includeTray)
    {
        string? data = GameDataFolder;
        var saves = Saves.Select(s => s.Model).Where(s => s.Slot >= 0).ToList();
        int keep = KeepPerSlot;
        var parts = await Task.Run(() =>
        {
            var parts = new List<string>();
            foreach (var save in saves)
                _backups.Create(save, automatic: true);
            int pruned = _backups.Prune(keep);
            parts.Add(L.F("{0} Spielstand/-stände gesichert", saves.Count) + (pruned > 0 ? " " + L.F("({0} alte Sicherungen entfernt)", pruned) : ""));
            if (includeTray && data is not null)
            {
                var result = _trayMirror.Update(Path.Combine(data, "Tray"));
                parts.Add(L.F("Bibliothek gespiegelt ({0} neu/geändert)", result.Copied));
            }
            return parts;
        });
        _main.Settings.TryUpdate(s => s.LastAutoBackupUtc = DateTime.UtcNow);
        RefreshBackups();
        return string.Join(", ", parts);
    }

    /// <summary>At startup: runs the scheduled backup when it is due.</summary>
    public async Task RunScheduledBackupIfDueAsync()
    {
        var settings = _main.Settings.Load();
        if (settings.AutoBackupIntervalDays <= 0 || Core.Game.GameInfo.IsGameRunning())
            return;
        if (settings.LastAutoBackupUtc is { } last && last > DateTime.UtcNow.AddDays(-settings.AutoBackupIntervalDays))
            return;
        if (Saves.Count == 0)
            await RefreshAsync();
        try
        {
            string done = await RunAutomaticBackupAsync(settings.BackupTrayBeforePlay);
            string message = L.F("Automatische Sicherung: {0}.", done);
            _main.StatusMessage = message;
            _main.ShowToast(message, ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string message = L.F("Automatische Sicherung fehlgeschlagen: {0}", ex.Message);
            _main.StatusMessage = message;
            _main.ShowToast(message, ToastKind.Error);
        }
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        string? data = GameDataFolder;
        bool tray = BackupTrayBeforePlay;
        if (tray && data is not null && !_trayMirror.Exists)
        {
            long bytes = await Task.Run(() => _trayMirror.PendingBytes(Path.Combine(data, "Tray")));
            if (!await _dialogs.ConfirmAsync(L.T("Bibliothek spiegeln"),
                    L.F("Beim ersten Mal wird die ganze Bibliothek kopiert ({0}). Danach werden nur neue oder geänderte Dateien kopiert.", Formatting.Size(bytes)),
                    L.T("Sichern")))
                return;
        }
        IsBusy = true;
        try
        {
            _main.StatusMessage = L.F("Gesichert: {0}.", await RunAutomaticBackupAsync(tray));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.StatusMessage = L.F("Sicherung fehlgeschlagen: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync(SaveBackupViewModel? vm)
    {
        var backup = vm?.Model;
        if (backup is null || GameDataFolder is not { } data || !await _main.EnsureGameClosedAsync())
            return;
        if (!await _dialogs.ConfirmAsync(L.T("Spielstand wiederherstellen"),
                L.F("Die Sicherung vom {0} ({1} Datei(en)) zurückspielen? Der aktuelle Spielstand im selben Slot wird vorher gesichert und lässt sich über den Verlauf zurückholen.",
                    backup.CreatedLocal.ToString("g", L.Culture), backup.FileCount),
                L.T("Wiederherstellen")))
            return;

        using (var recorder = _main.Journal.Begin(L.F("Spielstand wiederhergestellt: Sicherung vom {0}", backup.CreatedLocal.ToString("g", L.Culture))))
            _backups.Restore(backup, data, recorder);
        _main.History.Refresh(selectNewest: true);
        await RefreshAsync();
        _main.StatusMessage = L.T("Spielstand wiederhergestellt.") + " " + MainViewModel.UndoHint;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        string? path = SelectedSave?.Model.Path;
        if (path is not null && File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        Directory.CreateDirectory(_backups.Root);
        Process.Start("explorer.exe", $"\"{_backups.Root}\"");
    }

    [RelayCommand]
    private void OpenTrayMirror()
    {
        Directory.CreateDirectory(_trayMirror.Root);
        Process.Start("explorer.exe", $"\"{_trayMirror.Root}\"");
    }
}

public sealed class SaveBackupViewModel
{
    public SaveBackupViewModel(SaveBackup backup) => Model = backup;

    public SaveBackup Model { get; }
    public string Label => L.F("{0} · Slot {1}", Model.CreatedLocal.ToString("g", L.Culture), Model.Slot) +
                           (string.IsNullOrEmpty(Model.Name) ? "" : $" · {Model.Name}") +
                           (Model.IsAutomatic ? " · " + L.T("automatisch") : "");
}

public sealed partial class SaveViewModel : ObservableObject
{
    public SaveViewModel(SaveGameInfo save) => Model = save;

    public SaveGameInfo Model { get; }

    public string Label => Model.Slot < 0 ? L.T("Zwischenstand") : L.F("Slot {0}", Model.Slot);
    public string Title => string.IsNullOrWhiteSpace(Model.Name) ? Label : $"{Label} · {Model.Name}";
    public string DateLabel => Model.LastWriteUtc.ToLocalTime().ToString("g", L.Culture);
    public string SizeLabel => Formatting.Size(Model.SizeBytes);
    public string VersionLabel => Model.SavedWithVersion is null ? "?" : Core.Game.GameInfo.ShortVersion(Model.SavedWithVersion);
    public string SubtitleLabel => L.F("{0} · Spielversion {1} · {2} · CC: {3}", DateLabel, VersionLabel, SizeLabel, CcLabel);
    public string ContentLabel => Model.IsReadable
        ? L.F("{0} Haushalte · {1} Sims · {2} Grundstücke · {3} ältere Versionen", Model.HouseholdNames.Count, Model.SimCount, Model.LotCount, Model.VersionFiles.Count)
        : L.T("Inhalt nicht lesbar");
    public string CreatedLabel => Model.CreatedWithVersion is null ? string.Empty : L.F("Angelegt mit Spielversion {0}", Core.Game.GameInfo.ShortVersion(Model.CreatedWithVersion));
    public IReadOnlyList<string> Households => Model.HouseholdNames;

    [ObservableProperty] private IReadOnlyList<TrayCcReferenceViewModel> cc = Array.Empty<TrayCcReferenceViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleLabel))]
    private string ccLabel = L.T("wird ermittelt …");

    public void SetCc(IReadOnlyList<TrayCcReference> references)
    {
        Cc = references.Select(r => new TrayCcReferenceViewModel(r)).ToList();
        int disabled = references.Count(r => !r.IsEnabled);
        string files = references.Count == 1 ? L.T("1 Datei") : L.F("{0} Dateien", references.Count);
        CcLabel = references.Count == 0 ? L.T("kein CC") : disabled == 0 ? files : L.F("{0} ({1} deaktiviert!)", files, disabled);
    }
}
