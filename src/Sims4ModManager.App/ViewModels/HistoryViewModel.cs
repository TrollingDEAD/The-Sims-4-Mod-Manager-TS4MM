using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>The "Verlauf" tab: every recorded change set with undo, and "what changed since …" from snapshots.</summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private int _changesVersion;

    public HistoryViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;
    }

    public ObservableCollection<ChangeSetViewModel> Entries { get; } = new();

    [ObservableProperty]
    private ChangeSetViewModel? selectedEntry;

    public string BackupFolder => _main.Journal.RootDirectory;

    [RelayCommand]
    public void Refresh() => Refresh(selectNewest: false);

    /// <summary>Reloads the journal; after a new action the newest entry is selected so "Rückgängig" targets it.</summary>
    public void Refresh(bool selectNewest)
    {
        string? selectedId = SelectedEntry?.Model.Id;
        Entries.Clear();
        foreach (var set in _main.Journal.List())
            Entries.Add(new ChangeSetViewModel(set));
        SelectedEntry = selectNewest
            ? Entries.FirstOrDefault()
            : Entries.FirstOrDefault(e => e.Model.Id == selectedId) ?? Entries.FirstOrDefault();
    }

    [RelayCommand]
    private Task UndoSelectedAsync() => UndoAsync(SelectedEntry);

    /// <summary>Ctrl+Z: undo the most recent change that has not been undone yet.</summary>
    [RelayCommand]
    private Task UndoLatestAsync()
    {
        Refresh();
        return UndoAsync(Entries.FirstOrDefault(e => !e.Model.IsUndone));
    }

    private async Task UndoAsync(ChangeSetViewModel? entry)
    {
        if (entry is null)
        {
            _main.StatusMessage = L.T("Es gibt nichts rückgängig zu machen.");
            return;
        }
        if (entry.Model.IsUndone)
        {
            await _dialogs.ShowAsync(L.T("Rückgängig machen"), L.T("Dieser Eintrag wurde bereits rückgängig gemacht."));
            return;
        }

        if (!await _dialogs.ConfirmAsync(L.T("Rückgängig machen"),
                L.F("„{0}“ vom {1} rückgängig machen?", entry.Description, entry.DateLabel) + Environment.NewLine + Environment.NewLine +
                L.F("{0} werden zurückgesetzt. Dateien, die seitdem erneut verändert wurden, werden nicht überschrieben.", entry.OperationsLabel),
                L.T("Rückgängig machen")))
            return;

        var result = _main.Journal.Undo(entry.Model.Id);
        _main.RescanMods();
        Refresh(selectNewest: false);
        _main.StatusMessage = result.Success
            ? L.F("„{0}“ rückgängig gemacht ({1} Datei(en)).", entry.Description, result.Restored)
            : L.F("Teilweise rückgängig gemacht ({0} Datei(en)); {1} Problem(e): {2}", result.Restored, result.Errors.Count, string.Join("; ", result.Errors.Take(2)));
    }

    [RelayCommand]
    private void OpenFolder()
    {
        string folder = SelectedEntry is null ? BackupFolder : _main.Journal.GetDirectory(SelectedEntry.Model);
        if (Directory.Exists(folder))
            Process.Start("explorer.exe", $"\"{folder}\"");
    }

    // --- "Was hat sich geändert?" ---------------------------------------------------------------------

    public ObservableCollection<SnapshotOptionViewModel> Snapshots { get; } = new();
    public ObservableCollection<SnapshotChangeViewModel> Changes { get; } = new();

    [ObservableProperty] private SnapshotOptionViewModel? selectedSnapshot;
    [ObservableProperty] private string changesSummary = L.T("Noch kein Vergleichsstand – er wird täglich und vor jedem Spielstart angelegt.");
    [ObservableProperty] private bool isComparing;

    partial void OnSelectedSnapshotChanged(SnapshotOptionViewModel? value) => ShowChanges();

    /// <summary>Loads the snapshot list (in the background; they can be large).</summary>
    public async Task RefreshChangesAsync()
    {
        int version = ++_changesVersion;
        string? modsPath = _main.ModsPath;
        if (modsPath is null)
            return;
        IsComparing = true;
        try
        {
            var list = await Task.Run(() => _main.Snapshots.List(modsPath));
            if (version != _changesVersion)
                return;
            string? keep = SelectedSnapshot?.Model.Id;
            Snapshots.Clear();
            foreach (var snapshot in list)
                Snapshots.Add(new SnapshotOptionViewModel(snapshot));
            // Default: the last game start, else the oldest daily state (usually "yesterday").
            SelectedSnapshot = Snapshots.FirstOrDefault(s => s.Model.Id == keep)
                               ?? Snapshots.FirstOrDefault(s => s.Model.Reason == ModSnapshotStore.ReasonPlay)
                               ?? Snapshots.Skip(1).FirstOrDefault() ?? Snapshots.FirstOrDefault();
            if (SelectedSnapshot is null)
                ShowChanges();
        }
        finally
        {
            if (version == _changesVersion)
                IsComparing = false;
        }
    }

    private void ShowChanges()
    {
        Changes.Clear();
        if (SelectedSnapshot is null || _main.ModsPath is null)
        {
            ChangesSummary = L.T("Noch kein Vergleichsstand – er wird täglich und vor jedem Spielstart angelegt.");
            return;
        }

        var now = ModSnapshot.Capture(_main.ModsPath, _main.CurrentMods, string.Empty);
        var diff = ModSnapshotStore.Compare(SelectedSnapshot.Model, now);
        void Add(string kind, string symbol, IEnumerable<string> paths)
        {
            foreach (string path in paths)
                Changes.Add(new SnapshotChangeViewModel(kind, symbol, path));
        }
        Add(L.T("Neu"), "Add24", diff.Added.Select(f => f.Path));
        Add(L.T("Entfernt"), "Delete24", diff.Removed.Select(f => f.Path));
        Add(L.T("Geändert"), "Edit24", diff.Changed.Select(f => f.Path));
        Add(L.T("Aktiviert"), "CheckmarkCircle24", diff.Enabled.Select(f => f.Path));
        Add(L.T("Deaktiviert"), "DismissCircle24", diff.Disabled.Select(f => f.Path));
        Add(L.T("Verschoben"), "ArrowMove24", diff.Moved.Select(m => $"{m.From} → {m.To}"));

        ChangesSummary = diff.Total == 0
            ? L.F("Seit {0} hat sich nichts geändert.", SelectedSnapshot.ShortLabel)
            : L.F("Seit {0}: {1} neu, {2} entfernt, {3} geändert, {4} aktiviert, {5} deaktiviert, {6} verschoben.", SelectedSnapshot.ShortLabel,
                diff.Added.Count, diff.Removed.Count, diff.Changed.Count, diff.Enabled.Count, diff.Disabled.Count, diff.Moved.Count);
    }

    [RelayCommand]
    private Task RefreshChanges() => RefreshChangesAsync();
}

public sealed class SnapshotOptionViewModel
{
    public SnapshotOptionViewModel(ModSnapshot snapshot) => Model = snapshot;

    public ModSnapshot Model { get; }

    private string ReasonLabel => Model.Reason == ModSnapshotStore.ReasonPlay ? L.T("Spielstart") : L.T("Tagesstand");
    public string Label => $"{ReasonLabel} – {Model.CreatedUtc.ToLocalTime().ToString("g", L.Culture)} ({L.F("{0} Dateien", Model.Files.Count)})";
    public string ShortLabel => L.F("{0} vom {1}", ReasonLabel, Model.CreatedUtc.ToLocalTime().ToString("g", L.Culture));
}

public sealed record SnapshotChangeViewModel(string Kind, string Symbol, string Path);

public sealed class ChangeSetViewModel
{
    public ChangeSetViewModel(ChangeSet set)
    {
        Model = set;
        Operations = set.Operations.Select(op => new ChangeOperationViewModel(op)).ToList();
    }

    public ChangeSet Model { get; }
    public IReadOnlyList<ChangeOperationViewModel> Operations { get; }

    /// <summary>Descriptions are stored in the language used at the time; known German ones are translated.</summary>
    public string Description => L.T(Model.Description);
    public string DateLabel => Model.CreatedUtc.ToLocalTime().ToString("g", L.Culture);
    public bool IsUndone => Model.IsUndone;
    public bool IsIncomplete => !Model.IsUndone && !Model.Completed;
    public string StatusLabel => Model.IsUndone
        ? L.F("rückgängig gemacht ({0})", Model.UndoneUtc!.Value.ToLocalTime().ToString("g", L.Culture))
        : Model.Completed ? string.Empty : L.T("unvollständig (abgebrochen)");

    public string OperationsLabel
    {
        get
        {
            var parts = Model.Operations.GroupBy(o => o.Kind).Select(g => g.Key switch
            {
                ChangeKind.Moved => L.F("{0} verschoben/umbenannt", g.Count()),
                ChangeKind.Modified => L.F("{0} geändert", g.Count()),
                ChangeKind.Created => L.F("{0} neu", g.Count()),
                ChangeKind.DirectoryCreated => L.F("{0} Ordner angelegt", g.Count()),
                ChangeKind.DirectoryDeleted => L.F("{0} Ordner entfernt", g.Count()),
                _ => L.F("{0} entfernt", g.Count())
            });
            return L.F("{0} Datei(en): {1}", Model.Operations.Count, string.Join(", ", parts));
        }
    }
}

public sealed class ChangeOperationViewModel
{
    public ChangeOperationViewModel(ChangeOperation op)
    {
        bool disabledNow = op.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        bool disabledBefore = op.OriginalPath?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true;
        KindLabel = op.Kind switch
        {
            ChangeKind.Moved when disabledNow && !disabledBefore => L.T("Deaktiviert"),
            ChangeKind.Moved when disabledBefore && !disabledNow => L.T("Aktiviert"),
            ChangeKind.Moved => L.T("Verschoben"),
            ChangeKind.Modified => L.T("Geändert"),
            ChangeKind.Created => L.T("Neu"),
            ChangeKind.DirectoryCreated => L.T("Ordner neu"),
            ChangeKind.DirectoryDeleted => L.T("Ordner entfernt"),
            _ => L.T("Entfernt")
        };
        FileName = Path.GetFileName(op.Path);
        Details = op.Kind == ChangeKind.Moved ? $"{op.OriginalPath} → {op.Path}" : op.Path;
    }

    public string KindLabel { get; }
    public string FileName { get; }
    public string Details { get; }
}
