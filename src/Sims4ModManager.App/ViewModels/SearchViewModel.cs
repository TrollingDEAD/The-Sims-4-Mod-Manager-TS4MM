using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>Search across mods (names, creators, notes, tags, in-game names), the library and the saves.</summary>
public partial class SearchViewModel : ObservableObject
{
    private const int MaxPerKind = 8;
    private readonly MainViewModel _main;

    public SearchViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<SearchResultViewModel> Results { get; } = new();

    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private string resultSummary = string.Empty;

    partial void OnQueryChanged(string value) => Run();

    private void Run()
    {
        Results.Clear();
        string q = Query.Trim();
        if (q.Length < 2)
        {
            IsOpen = false;
            return;
        }

        bool Has(string? text) => text?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false;

        var mods = _main.Mods
            .Where(m => Has(m.DisplayName) || Has(m.CreatorLabel) || Has(m.CategoryLabel) || Has(m.NoteTooltip) || m.Tags.Any(Has)
                        || m.Model.Files.Any(f => Has(_main.Catalog.InfoOf(f)?.Name)))
            .ToList();
        foreach (var mod in mods.Take(MaxPerKind))
        {
            string? inGame = mod.Model.Files.Select(f => _main.Catalog.InfoOf(f)?.Name).FirstOrDefault(n => Has(n));
            string subtitle = string.Join(" · ", new[] { mod.CategoryLabel, mod.CreatorLabel, inGame is null ? null : $"„{inGame}“" }
                .Where(s => !string.IsNullOrEmpty(s)));
            Results.Add(new SearchResultViewModel(L.T("Mod"), mod.DisplayName, subtitle, () => _main.ShowMod(mod.Model)));
        }

        var tray = _main.Tray.Items.Where(i => Has(i.SearchText)).ToList();
        foreach (var item in tray.Take(MaxPerKind))
            Results.Add(new SearchResultViewModel(L.T("Bibliothek"), item.Name, $"{item.TypeLabel} · {item.Creator}", () =>
            {
                _main.SelectedTabIndex = MainViewModel.TabLibrary;
                _main.Tray.SelectedItem = item;
            }));

        var saves = _main.Saves.Saves.Where(s => Has(s.Title) || s.Households.Any(Has)).ToList();
        foreach (var save in saves.Take(MaxPerKind))
            Results.Add(new SearchResultViewModel(L.T("Spielstand"), save.Title, string.Join(", ", save.Households.Take(4)), () =>
            {
                _main.SelectedTabIndex = MainViewModel.TabSaves;
                _main.Saves.SelectedSave = save;
            }));

        int total = mods.Count + tray.Count + saves.Count;
        ResultSummary = total == 0
            ? L.F("Nichts gefunden für „{0}“.", q)
            : L.F("{0} Mods, {1} Bibliothekseinträge, {2} Spielstände", mods.Count, tray.Count, saves.Count);
        IsOpen = true;
    }

    [RelayCommand]
    private void Open(SearchResultViewModel? result)
    {
        if (result is null)
            return;
        IsOpen = false;
        result.Navigate();
    }

    [RelayCommand]
    private void Clear()
    {
        Query = string.Empty;
        IsOpen = false;
    }
}

public sealed class SearchResultViewModel
{
    private readonly Action _navigate;

    public SearchResultViewModel(string kind, string title, string subtitle, Action navigate)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        _navigate = navigate;
    }

    public string Kind { get; }
    public string Title { get; }
    public string Subtitle { get; }

    public void Navigate() => _navigate();
}
