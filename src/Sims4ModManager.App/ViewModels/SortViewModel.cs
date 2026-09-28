using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>"Mods-Ordner sortieren": preview of the target structure, then one undoable step.</summary>
public partial class SortViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private SortPlan? _plan;

    public SortViewModel(MainViewModel main)
    {
        _main = main;
        byCreator = main.Settings.Load().SortByCreator;
        Rebuild();
    }

    public ObservableCollection<SortGroupViewModel> Groups { get; } = new();
    public ObservableCollection<string> Skipped { get; } = new();

    [ObservableProperty] private bool byCreator;
    [ObservableProperty] private bool includeFolders = true;
    [ObservableProperty] private bool resort;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private string skippedHeader = string.Empty;
    [ObservableProperty] private bool canApply;
    [ObservableProperty] private bool isBusy;

    /// <summary>Asks the window to close (true = sorted).</summary>
    public event Action<bool>? CloseRequested;

    partial void OnByCreatorChanged(bool value)
    {
        _main.Settings.TryUpdate(s => s.SortByCreator = value);
        Rebuild();
    }

    partial void OnIncludeFoldersChanged(bool value) => Rebuild();
    partial void OnResortChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        if (_main.ModsPath is null)
            return;
        var inputs = _main.CurrentMods.Select(m =>
        {
            var (category, cas) = _main.Catalog.CategoryOf(m);
            return new SortInput(m, category, cas, _main.Catalog.CreatorOf(m));
        }).ToList();
        _plan = ModSorter.Plan(_main.ModsPath, inputs, new SortOptions { ByCreator = ByCreator, IncludeFolders = IncludeFolders, Resort = Resort });

        Groups.Clear();
        foreach (var byCategory in _plan.Moves.GroupBy(m => m.TargetFolder.Split(System.IO.Path.DirectorySeparatorChar)[0])
                     .OrderByDescending(g => g.Count()))
        {
            var subfolders = byCategory.GroupBy(m => m.TargetFolder).Count();
            string examples = string.Join(", ", byCategory.Take(4).Select(m => m.Mod.DisplayName)) + (byCategory.Count() > 4 ? ", …" : "");
            Groups.Add(new SortGroupViewModel(
                byCategory.Key,
                ByCreator && subfolders > 1 ? L.F("{0} Mods in {1} Ersteller-Ordnern", byCategory.Count(), subfolders) : L.F("{0} Mods", byCategory.Count()),
                examples));
        }

        Skipped.Clear();
        foreach (var reason in _plan.Skipped.GroupBy(s => s.Reason).OrderByDescending(g => g.Count()))
            Skipped.Add($"{reason.Count()}× {reason.Key}" + (reason.Count() <= 3 ? $" ({string.Join(", ", reason.Select(s => s.Mod.DisplayName))})" : ""));

        Summary = _plan.Moves.Count == 0
            ? L.T("Es gibt nichts zu sortieren.")
            : L.F("{0} Mods ({1} Dateien) werden in {2} Kategorie-Ordner verschoben.", _plan.Moves.Count, _plan.FileCount, Groups.Count);
        SkippedHeader = L.F("Bleiben, wo sie sind: {0}", _plan.Skipped.Count);
        CanApply = _plan.Moves.Count > 0;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_plan is null || _plan.Moves.Count == 0 || !await _main.EnsureGameClosedAsync())
            return;

        IsBusy = true;
        SortResult result;
        try
        {
            var plan = _plan;
            result = await Task.Run(() =>
            {
                using var recorder = _main.Journal.Begin(L.F("Mods-Ordner sortiert ({0} Mods)", plan.Moves.Count));
                return ModSorter.Apply(plan, recorder);
            });
        }
        finally
        {
            IsBusy = false;
        }

        _main.AfterChange();
        _main.StatusMessage = L.F("{0} Mods einsortiert.", result.MovedMods) +
                              (result.Errors.Count > 0 ? " " + L.F("{0} Fehler: {1}", result.Errors.Count, string.Join("; ", result.Errors.Take(2))) : "") +
                              " " + MainViewModel.UndoHint;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}

public sealed record SortGroupViewModel(string Folder, string CountLabel, string Examples);
