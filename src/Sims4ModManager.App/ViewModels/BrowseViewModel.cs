using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Online;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// The "Browse" sub-tab of "CurseForge": search/browse the Sims 4 catalog and install with one
/// click. Required and optional dependencies are installed automatically alongside the mod, and the
/// result is sorted into its category/creator folder the same way a manual "Sortieren" pass would -
/// install + dependencies + sort all undo as a single History entry. Shares the API key entered on
/// the "Updates" sub-tab (<see cref="UpdatesViewModel.CreateClient"/>) rather than asking twice.
/// </summary>
public partial class BrowseViewModel : ObservableObject
{
    private const int PageSize = 30;

    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private readonly bool _initializing = true;
    private CancellationTokenSource? _searchCancel;
    private int? _modsClassId;
    private bool _categoriesLoaded;
    private int _nextIndex;
    private int _totalCount;

    public BrowseViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;

        CategoryOptions.Add(new BrowseCategoryOption(null, L.T("Alle Kategorien")));
        SelectedCategory = CategoryOptions[0];
        SortChoices = new[]
        {
            new BrowseSortOption(CurseForgeClient.SortField.Featured, L.T("Empfohlen")),
            new BrowseSortOption(CurseForgeClient.SortField.Popularity, L.T("Beliebtheit")),
            new BrowseSortOption(CurseForgeClient.SortField.LastUpdated, L.T("Zuletzt aktualisiert")),
            new BrowseSortOption(CurseForgeClient.SortField.Name, L.T("Name"))
        };
        SelectedSort = SortChoices[0];
        _initializing = false;
    }

    public ObservableCollection<BrowseModTileViewModel> Results { get; } = new();
    public ObservableCollection<BrowseCategoryOption> CategoryOptions { get; } = new();
    public IReadOnlyList<BrowseSortOption> SortChoices { get; }

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private BrowseCategoryOption? selectedCategory;
    [ObservableProperty] private BrowseSortOption selectedSort;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private string busyText = string.Empty;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private bool hasMore;

    partial void OnSearchTextChanged(string value)
    {
        if (!_initializing) _ = SearchAsync();
    }

    partial void OnSelectedCategoryChanged(BrowseCategoryOption? value)
    {
        if (!_initializing) _ = SearchAsync();
    }

    partial void OnSelectedSortChanged(BrowseSortOption value)
    {
        if (!_initializing) _ = SearchAsync();
    }

    /// <summary>
    /// Loads the category list (cached - see <see cref="CurseForgeCategoryCache"/>) and shows an
    /// initial listing, the first time the Browse tab is actually opened. Never called from the
    /// constructor, so opening the app never hits the network for this on its own.
    /// </summary>
    public async Task EnsureCategoriesLoadedAsync()
    {
        if (!_categoriesLoaded && _main.Updates.CreateClient() is { } client)
        {
            try
            {
                var data = await CurseForgeCategoryCache.LoadAsync(client);
                _modsClassId = data.ModsClassId;
                foreach (var category in data.Categories)
                    CategoryOptions.Add(new BrowseCategoryOption(category.Id, category.Name));
                _categoriesLoaded = true;
            }
            catch (CurseForgeException)
            {
                // Retried next visit; the category filter just stays unavailable meanwhile - search itself still works.
            }
        }
        if (Results.Count == 0 && !IsBusy)
            await SearchAsync();
    }

    [RelayCommand]
    private Task Search() => SearchAsync();

    private async Task SearchAsync()
    {
        if (_main.Updates.CreateClient() is not { } client)
            return;

        _searchCancel?.Cancel();
        var cancel = _searchCancel = new CancellationTokenSource();
        Results.Clear();
        _nextIndex = 0;
        IsBusy = true;
        BusyText = L.T("Wird gesucht …");
        try
        {
            var (mods, total) = await client.SearchModsAsync(
                SearchText, _modsClassId, SelectedCategory?.Id, SelectedSort.Field, index: 0, pageSize: PageSize, cancel.Token);
            if (cancel.IsCancellationRequested)
                return;
            _totalCount = total;
            _nextIndex = mods.Count;
            foreach (var mod in mods)
                Results.Add(new BrowseModTileViewModel(mod));
            HasMore = _nextIndex < _totalCount;
            Summary = _totalCount == 0 ? L.T("Keine Treffer – Suchbegriff oder Kategorie ändern.") : L.F("{0} von {1} Projekt(en)", Results.Count, _totalCount);
        }
        catch (CurseForgeException ex)
        {
            Summary = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
        finally
        {
            if (!cancel.IsCancellationRequested)
                IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoadingMore || _main.Updates.CreateClient() is not { } client)
            return;
        IsLoadingMore = true;
        try
        {
            var (mods, total) = await client.SearchModsAsync(SearchText, _modsClassId, SelectedCategory?.Id, SelectedSort.Field, index: _nextIndex, pageSize: PageSize);
            _totalCount = total;
            _nextIndex += mods.Count;
            foreach (var mod in mods)
                Results.Add(new BrowseModTileViewModel(mod));
            HasMore = _nextIndex < _totalCount;
            Summary = L.F("{0} von {1} Projekt(en)", Results.Count, _totalCount);
        }
        catch (CurseForgeException ex)
        {
            _main.StatusMessage = ex.Message;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private void OpenPage(BrowseModTileViewModel? vm) => OpenUrl(vm?.Model.Links.WebsiteUrl);

    [RelayCommand]
    private async Task InstallAsync(BrowseModTileViewModel? vm)
    {
        if (vm is null || _main.ModsPath is not { } modsPath || _main.Updates.CreateClient() is not { } client)
            return;
        if (!await _main.EnsureGameClosedAsync())
            return;

        if (CurseForgeDependencyResolver.PickBestFile(vm.Model.LatestFiles) is not { } rootFile)
        {
            await _dialogs.ShowAsync(L.T("Installieren"), L.T("Für dieses Projekt gibt es keine passende Datei."));
            return;
        }

        IsBusy = true;
        BusyText = L.T("Abhängigkeiten werden ermittelt …");
        var downloaded = new List<(string Path, string FileName)>();
        try
        {
            var (chain, blocked) = await CurseForgeDependencyResolver.ResolveAsync(client, new[] { (vm.Model, rootFile) });
            if (blocked.Count > 0)
            {
                await _dialogs.ShowAsync(L.T("Installieren"),
                    L.F("„{0}“ erlaubt Downloads nur auf der CurseForge-Seite und lässt sich deshalb nicht automatisch installieren. Der Vorgang wird abgebrochen.",
                        string.Join(", ", blocked.Select(m => m.Name))));
                return;
            }

            var existingNames = _main.CurrentMods.SelectMany(m => m.Files)
                .Select(f => Path.GetFileName(ModFileNaming.ToEnabledPath(f.AbsolutePath)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (existingNames.Contains(rootFile.FileName) &&
                !await _dialogs.ConfirmAsync(L.T("Installieren"),
                    L.F("„{0}“ scheint schon installiert zu sein ({1}). Trotzdem installieren?", vm.Name, rootFile.FileName), L.T("Installieren")))
                return;

            var toDownload = chain.Where(c => c.IsRoot || !existingNames.Contains(c.File.FileName)).ToList();
            int skippedCount = chain.Count - toDownload.Count;

            BusyText = L.T("Wird heruntergeladen …");
            foreach (var item in toDownload)
            {
                string? url = item.File.DownloadUrl ?? await client.GetDownloadUrlAsync(item.ModId, item.File.Id);
                if (url is null)
                    continue; // AllowModDistribution already screened out blocked projects above.
                string temp = Path.Combine(Path.GetTempPath(), "Sims4ModManager", "cfbrowse-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                await client.DownloadAsync(url, temp);
                downloaded.Add((temp, item.File.FileName));
            }

            BusyText = L.T("Wird installiert und einsortiert …");
            string description = chain.Count > 1
                ? L.F("Installiert: {0} (+{1} Abhängigkeit(en))", vm.Name, chain.Count - 1)
                : L.F("Installiert: {0}", vm.Name);
            var (installedCount, sortedCount) = await Task.Run(() =>
            {
                using var recorder = _main.Journal.Begin(description);
                var installedPaths = new List<string>();
                foreach (var (path, name) in downloaded)
                    installedPaths.AddRange(CurseForgeUpdateChecker.ApplyInstall(recorder, modsPath, path, name));

                var allMods = ModScanner.Scan(modsPath);
                var installedSet = installedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newMods = allMods.Where(m => m.Files.Any(f => installedSet.Contains(f.AbsolutePath))).ToList();
                if (newMods.Count == 0)
                    return (installedPaths.Count, 0);

                var info = new CatalogScanner().Scan(newMods);
                var (creators, _) = CatalogViewModel.GuessCreators(newMods, info);
                var inputs = newMods.Select(m =>
                {
                    var (category, cas) = ModClassifier.ClassifyMod(m, info);
                    return new SortInput(m, category, cas, creators.GetValueOrDefault(m.Id));
                }).ToList();

                var options = new SortOptions { ByCreator = _main.Settings.Load().SortByCreator };
                var plan = ModSorter.Plan(modsPath, inputs, options);
                var sortResult = ModSorter.Apply(plan, recorder);
                return (installedPaths.Count, sortResult.MovedMods);
            });

            _main.AfterChange();
            string toast = chain.Count > 1
                ? L.F("„{0}“ installiert (+{1} Abhängigkeit(en)).", vm.Name, chain.Count - 1)
                : L.F("„{0}“ installiert.", vm.Name);
            if (sortedCount > 0)
                toast += " " + L.T("Einsortiert.");
            if (skippedCount > 0)
                toast += " " + L.F("{0} Abhängigkeit(en) war(en) schon vorhanden.", skippedCount);
            _main.ShowToast(toast + " " + MainViewModel.UndoHint, ToastKind.Success);
        }
        catch (Exception ex) when (ex is CurseForgeException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _dialogs.ShowAsync(L.T("Installieren"), ex.Message);
        }
        finally
        {
            foreach (var (path, _) in downloaded)
            {
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp file */ }
            }
            IsBusy = false;
        }
    }

    private static void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}

public sealed record BrowseCategoryOption(int? Id, string Label);

public sealed record BrowseSortOption(CurseForgeClient.SortField Field, string Label);

public sealed class BrowseModTileViewModel
{
    public BrowseModTileViewModel(CurseForgeMod model)
    {
        Model = model;
        Name = model.Name;
        Authors = string.Join(", ", model.Authors.Select(a => a.Name));
        CategoryLabel = model.Categories.FirstOrDefault()?.Name ?? string.Empty;
        LogoUrl = model.Logo?.ThumbnailUrl ?? model.Logo?.Url;
    }

    public CurseForgeMod Model { get; }
    public string Name { get; }
    public string Authors { get; }
    public string CategoryLabel { get; }
    public string? LogoUrl { get; }
}
