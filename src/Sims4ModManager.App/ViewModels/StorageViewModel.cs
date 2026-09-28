using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.App.ViewModels;

/// <summary>The "Speicherplatz" tab: where the space in the Mods folder goes.</summary>
public partial class StorageViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private int _version;
    private StorageReport? _report;

    public StorageViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<StorageBarViewModel> ByCategory { get; } = new();
    public ObservableCollection<StorageBarViewModel> ByCreator { get; } = new();
    public ObservableCollection<StorageBarViewModel> Largest { get; } = new();
    public ObservableCollection<DuplicateSetViewModel> Duplicates { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string totalLabel = "–";
    [ObservableProperty] private string disabledLabel = "–";
    [ObservableProperty] private string duplicatesLabel = "–";
    [ObservableProperty] private string uncompressedLabel = "–";
    [ObservableProperty] private bool hasDuplicates;

    public async Task RefreshAsync()
    {
        int version = ++_version;
        var mods = _main.CurrentMods;
        var categories = mods.ToDictionary(m => m, _main.Catalog.CategoryLabelOf);
        var creators = mods.ToDictionary(m => m, _main.Catalog.CreatorOf);
        IsBusy = true;
        DuplicatesLabel = L.T("wird berechnet …");
        try
        {
            var report = await Task.Run(() => StorageAnalyzer.Analyze(mods, m => categories[m], m => creators[m]));
            if (version != _version)
                return;
            _report = report;
            Show(report);
        }
        finally
        {
            if (version == _version)
                IsBusy = false;
        }
    }

    private void Show(StorageReport report)
    {
        TotalLabel = L.F("{0} in {1} Dateien", Formatting.Size(report.TotalBytes), report.FileCount);
        DisabledLabel = Formatting.Size(report.DisabledBytes);
        DuplicatesLabel = report.Duplicates.Count == 0
            ? L.T("keine")
            : L.F("{0} in {1} Gruppen", Formatting.Size(report.WastedByDuplicates), report.Duplicates.Count);
        UncompressedLabel = Formatting.Size(report.UncompressedBytes);

        Fill(ByCategory, report.ByCategory.Select(b => (b.Label, b.Bytes, b.Count, (ModEntry?)null)), 14);
        Fill(ByCreator, report.ByCreator.Select(b => (b.Label, b.Bytes, b.Count, (ModEntry?)null)), 14);
        Fill(Largest, report.Largest.Select(m => (m.DisplayName, m.TotalSizeBytes, m.Files.Count, (ModEntry?)m)), 25);

        Duplicates.Clear();
        foreach (var set in report.Duplicates.Take(200))
            Duplicates.Add(new DuplicateSetViewModel(set));
        HasDuplicates = Duplicates.Count > 0;
    }

    private static void Fill(ObservableCollection<StorageBarViewModel> target, IEnumerable<(string Label, long Bytes, int Count, ModEntry? Mod)> items, int take)
    {
        var list = items.Take(take).ToList();
        long max = list.Count == 0 ? 1 : Math.Max(1, list.Max(i => i.Bytes));
        target.Clear();
        foreach (var item in list)
            target.Add(new StorageBarViewModel(item.Label, item.Bytes, item.Count, (double)item.Bytes / max, item.Mod));
    }

    [RelayCommand]
    private void ShowMod(StorageBarViewModel? bar)
    {
        if (bar?.Mod is not null)
            _main.ShowMod(bar.Mod);
    }

    [RelayCommand]
    private Task CompressAllAsync() => _main.CompressAllAsync();

    /// <summary>Removes all but one copy of each identical file (into the backup, undoable).</summary>
    [RelayCommand]
    private async Task RemoveDuplicatesAsync()
    {
        if (_report is null || _report.Duplicates.Count == 0 || !await _main.EnsureGameClosedAsync())
            return;
        var surplus = _report.Duplicates.SelectMany(d => Surplus(d)).ToList();
        if (!await _main.Dialogs.ConfirmAsync(L.T("Doppelte Dateien entfernen"),
                L.F("{0} überzählige Kopie(n) ({1}) werden aus dem Mods-Ordner entfernt; von jeder Datei bleibt genau eine Kopie erhalten. Die entfernten Dateien liegen danach in der Sicherung und lassen sich über den Verlauf zurückholen.",
                    surplus.Count, Formatting.Size(surplus.Sum(f => f.SizeBytes))),
                L.T("Entfernen"), destructive: true))
            return;

        var errors = new List<string>();
        using (var recorder = _main.Journal.Begin(L.F("{0} doppelte Dateien entfernt", surplus.Count)))
        {
            foreach (var file in surplus)
            {
                try { recorder.Delete(file.AbsolutePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{Path.GetFileName(file.AbsolutePath)}: {ex.Message}"); }
            }
        }
        _main.AfterChange();
        _main.StatusMessage = L.F("{0} doppelte Datei(en) entfernt.", surplus.Count - errors.Count) +
                              (errors.Count > 0 ? " " + L.F("{0} Fehler: {1}", errors.Count, string.Join("; ", errors.Take(2))) : "") + " " + MainViewModel.UndoHint;
    }

    /// <summary>Keeps the enabled copy with the shortest path (usually the "original" location).</summary>
    internal static IEnumerable<ModFileInfo> Surplus(DuplicateSet set)
    {
        var keep = set.Files.OrderByDescending(f => f.File.IsEnabled).ThenBy(f => f.File.AbsolutePath.Length).First();
        return set.Files.Where(f => f.File != keep.File).Select(f => f.File);
    }
}

public sealed class StorageBarViewModel
{
    public StorageBarViewModel(string label, long bytes, int count, double fraction, ModEntry? mod)
    {
        Label = label;
        Bytes = bytes;
        Count = count;
        Fraction = Math.Max(0.01, fraction);
        Mod = mod;
    }

    public string Label { get; }
    public long Bytes { get; }
    public int Count { get; }
    public double Fraction { get; }
    public ModEntry? Mod { get; }
    public string SizeLabel => Formatting.Size(Bytes);
    public string CountLabel => Mod is null ? L.F("{0} Mods", Count) : L.F("{0} Datei(en)", Count);
}

public sealed class DuplicateSetViewModel
{
    public DuplicateSetViewModel(DuplicateSet set)
    {
        Model = set;
        var keep = set.Files.Except(set.Files.Where(f => StorageViewModel.Surplus(set).Contains(f.File))).First();
        Title = Path.GetFileName(ModFileNaming.ToEnabledPath(set.Files[0].File.AbsolutePath));
        Paths = set.Files.Select(f => (f.File == keep.File ? "✓ " : "✗ ") + f.File.AbsolutePath).ToList();
    }

    public DuplicateSet Model { get; }
    public string Title { get; }
    public IReadOnlyList<string> Paths { get; }
    public string WastedLabel => L.F("{0} Kopien · {1} verschwendet", Model.Files.Count, Formatting.Size(Model.WastedBytes));
}
