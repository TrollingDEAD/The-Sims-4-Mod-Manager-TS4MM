using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// The "Katalog" tab: every mod file as a tile with its in-game preview, name, category, creator and
/// age/gender. Also the source of category and creator for the mod list, sorting and the storage view.
/// </summary>
public partial class CatalogViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly CatalogScanner _scanner = new() { PreferredLanguage = L.IsGerman ? (byte)0x08 : (byte)0x00 };
    private IReadOnlyDictionary<ModFileInfo, PackageCatalogInfo> _info = new Dictionary<ModFileInfo, PackageCatalogInfo>();
    private IReadOnlyDictionary<string, string> _creators = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _fileCreators = new Dictionary<string, string>();
    private int _version;

    public static readonly string AllCategories = L.T("Alle Kategorien");
    public static readonly string AllAges = L.T("Alle Altersstufen");
    public static readonly string AllGenders = L.T("Alle");

    public CatalogViewModel(MainViewModel main)
    {
        _main = main;
        TilesView = CollectionViewSource.GetDefaultView(Tiles);
        TilesView.Filter = o => o is CatalogTileViewModel tile && Matches(tile);
    }

    public ObservableCollection<CatalogTileViewModel> Tiles { get; } = new();
    public ICollectionView TilesView { get; }
    public ObservableCollection<string> CategoryOptions { get; } = new() { AllCategories };

    public IReadOnlyList<string> AgeOptions { get; } = new[]
    {
        AllAges, L.T("Kleinkind"), L.T("Kind"), L.T("Teenager"), L.T("Erwachsene"), L.T("Senioren")
    };

    public IReadOnlyList<string> GenderOptions { get; } = new[] { AllGenders, L.T("Weiblich"), L.T("Männlich") };

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string summary = L.T("Katalog wird aufgebaut …");
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string categoryFilter = AllCategories;
    [ObservableProperty] private string ageFilter = AllAges;
    [ObservableProperty] private string genderFilter = AllGenders;
    [ObservableProperty] private bool onlyEnabled;
    [ObservableProperty] private CatalogTileViewModel? selectedTile;
    [ObservableProperty] private string countLabel = string.Empty;

    /// <summary>Raised after new catalog data arrived (mod list columns, storage and sorting use it).</summary>
    public event Action? Updated;

    partial void OnSearchTextChanged(string value) => RefreshView();
    partial void OnCategoryFilterChanged(string value) => RefreshView();
    partial void OnAgeFilterChanged(string value) => RefreshView();
    partial void OnGenderFilterChanged(string value) => RefreshView();
    partial void OnOnlyEnabledChanged(bool value) => RefreshView();

    private void RefreshView()
    {
        TilesView.Refresh();
        int visible = TilesView.Cast<object>().Count();
        CountLabel = visible == Tiles.Count ? L.F("{0} Dateien", Tiles.Count) : L.F("{0} von {1} Dateien", visible, Tiles.Count);
    }

    public bool HasData => _info.Count > 0;

    public async Task RefreshAsync()
    {
        int version = ++_version;
        var mods = _main.CurrentMods;
        IsBusy = true;
        Summary = L.T("Katalog wird aufgebaut …");
        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                if (version == _version && p.Done < p.Total)
                    Summary = L.F("Katalog wird aufgebaut … {0} von {1} Dateien gelesen", p.Done, p.Total);
            });
            var (info, creators, fileCreators) = await Task.Run(() =>
            {
                var info = _scanner.Scan(mods, progress);
                var (creators, fileCreators) = GuessCreators(mods, info);
                return (info, creators, fileCreators);
            });
            if (version != _version)
                return;

            _info = info;
            _creators = creators;
            _fileCreators = fileCreators;
            BuildTiles(mods);
            Updated?.Invoke();
        }
        finally
        {
            if (version == _version)
                IsBusy = false;
        }
    }

    /// <summary>
    /// Creators per file (file name + CAS part names) and per mod: a folder's own name ("[Creator] Set") wins,
    /// otherwise the creator of most of its files - a mixed folder ("Mods\CC") gets none instead of a wrong one.
    /// </summary>
    /// <summary>Internal so the CurseForge Browse tab can guess creators for freshly-installed mods the
    /// same way "Sortieren" would, instead of trusting CurseForge's own author field (which can differ
    /// from the folder-name convention this heuristic follows).</summary>
    internal static (IReadOnlyDictionary<string, string> Mods, IReadOnlyDictionary<string, string> Files) GuessCreators(
        IReadOnlyList<ModEntry> mods, IReadOnlyDictionary<ModFileInfo, PackageCatalogInfo> info)
    {
        var files = CreatorGuesser.Guess(mods.SelectMany(m => m.Files).Select(f => new CreatorGuesser.Input(
            f.AbsolutePath, Path.GetFileNameWithoutExtension(Core.ModFileNaming.ToEnabledPath(f.AbsolutePath)),
            info.TryGetValue(f, out var i) ? i.CasPartNames : Array.Empty<string>())));
        var byName = CreatorGuesser.Guess(mods.Where(m => m.IsFolder).Select(m => new CreatorGuesser.Input(m.Id, m.DisplayName, Array.Empty<string>())));

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            if (!mod.IsFolder)
            {
                if (files.TryGetValue(mod.Files[0].AbsolutePath, out var creator))
                    result[mod.Id] = creator;
                continue;
            }
            if (byName.TryGetValue(mod.Id, out var named))
            {
                result[mod.Id] = named;
                continue;
            }
            var top = mod.Files.Select(f => files.GetValueOrDefault(f.AbsolutePath)).OfType<string>()
                .GroupBy(c => c, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (top is not null && top.Count() * 2 >= mod.Files.Count)
                result[mod.Id] = top.Key;
        }
        return (result, files);
    }

    private void BuildTiles(IReadOnlyList<ModEntry> mods)
    {
        string? selectedPath = SelectedTile?.File.AbsolutePath;
        var tiles = mods
            .SelectMany(m => m.Files.Select(f => (Mod: m, File: f)))
            .Where(x => _info.ContainsKey(x.File))
            .Select(x => new CatalogTileViewModel(x.Mod, x.File, _info[x.File], CreatorOf(x.File) ?? CreatorOf(x.Mod), _scanner.ThumbnailDirectory))
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Tiles.Clear();
        foreach (var tile in tiles)
            Tiles.Add(tile);

        string keep = CategoryFilter;
        CategoryOptions.Clear();
        CategoryOptions.Add(AllCategories);
        foreach (var group in tiles.GroupBy(t => t.CategoryLabel).OrderByDescending(g => g.Count()))
            CategoryOptions.Add(group.Key);
        CategoryFilter = CategoryOptions.Contains(keep) ? keep : AllCategories;

        SelectedTile = tiles.FirstOrDefault(t => string.Equals(t.File.AbsolutePath, selectedPath, StringComparison.OrdinalIgnoreCase));
        int withPreview = tiles.Count(t => t.HasThumbnail);
        Summary = L.F("{0} Dateien von {1} Erstellern, {2} mit Vorschaubild.", tiles.Count, _fileCreators.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count(), withPreview);
        RefreshView();
    }

    private bool Matches(CatalogTileViewModel tile)
    {
        if (OnlyEnabled && !tile.File.IsEnabled)
            return false;
        if (CategoryFilter != AllCategories && tile.CategoryLabel != CategoryFilter)
            return false;
        if (AgeFilter != AllAges && !tile.HasAge(Array.IndexOf(AgeOptions.ToArray(), AgeFilter)))
            return false;
        if (GenderFilter != AllGenders && !tile.HasGender(GenderFilter == GenderOptions[1]))
            return false;
        string search = SearchText.Trim();
        return search.Length == 0
               || tile.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || tile.Mod.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || (tile.Creator?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    // --- Used by other tabs ------------------------------------------------------------------------

    public PackageCatalogInfo? InfoOf(ModFileInfo file) => _info.TryGetValue(file, out var info) ? info : null;

    public string? CreatorOf(ModEntry mod) => _creators.TryGetValue(mod.Id, out var creator) ? creator : null;

    public string? CreatorOf(ModFileInfo file) => _fileCreators.TryGetValue(file.AbsolutePath, out var creator) ? creator : null;

    public (ContentCategory Category, CasCategory Cas) CategoryOf(ModEntry mod) => ModClassifier.ClassifyMod(mod, _info);

    public string CategoryLabelOf(ModEntry mod)
    {
        var (category, cas) = CategoryOf(mod);
        return ContentCategories.Label(category, cas);
    }

    /// <summary>Preview image of a mod (its first file with one).</summary>
    public string? ThumbnailOf(ModEntry mod) => mod.Files.Select(InfoOf)
        .Select(i => i?.Thumbnail).FirstOrDefault(t => t is not null) is { } name
        ? Path.Combine(_scanner.ThumbnailDirectory, name)
        : null;

    // --- Commands ----------------------------------------------------------------------------------

    [RelayCommand]
    private void ShowInMods(CatalogTileViewModel? tile)
    {
        if (tile is not null)
            _main.ShowMod(tile.Mod);
    }

    [RelayCommand]
    private void OpenInExplorer(CatalogTileViewModel? tile)
    {
        string? path = (tile ?? SelectedTile)?.File.AbsolutePath;
        if (path is not null && File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    [RelayCommand]
    private void ToggleMod(CatalogTileViewModel? tile)
    {
        if (tile is null)
            return;
        var vm = _main.Mods.FirstOrDefault(m => m.Model == tile.Mod);
        if (vm is not null)
            vm.IsEnabled = !vm.IsEnabled; // goes through the normal toggle (journal, save warning)
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        CategoryFilter = AllCategories;
        AgeFilter = AllAges;
        GenderFilter = AllGenders;
        OnlyEnabled = false;
    }
}

public sealed partial class CatalogTileViewModel : ObservableObject
{
    private readonly string? _thumbnailPath;
    private bool _loading;
    private BitmapSource? _thumbnail;

    public CatalogTileViewModel(ModEntry mod, ModFileInfo file, PackageCatalogInfo info, string? creator, string thumbnailDirectory)
    {
        Mod = mod;
        File = file;
        Info = info;
        Creator = creator;
        _thumbnailPath = info.Thumbnail is null ? null : Path.Combine(thumbnailDirectory, info.Thumbnail);
    }

    public ModEntry Mod { get; }
    public ModFileInfo File { get; }
    public PackageCatalogInfo Info { get; }
    public string? Creator { get; }

    public string FileName => Path.GetFileNameWithoutExtension(Core.ModFileNaming.ToEnabledPath(File.AbsolutePath));
    public string Title => Info.Name ?? FileName;
    public string CategoryLabel => ContentCategories.Label(Info.Category, Info.CasCategory);
    public string Subtitle => Creator is null ? CategoryLabel : $"{Creator} · {CategoryLabel}";
    public bool IsEnabled => File.IsEnabled;
    public bool HasThumbnail => _thumbnailPath is not null;

    public string DetailsLabel
    {
        get
        {
            var parts = new List<string> { CategoryLabel };
            if (Info.ItemCount > 1)
                parts.Add(L.F("{0} Varianten", Info.ItemCount));
            string ages = ContentCategories.AgeLabel(Info.AgeGender);
            if (ages.Length > 0)
                parts.Add(ages);
            string gender = ContentCategories.GenderLabel(Info.AgeGender);
            if (gender.Length > 0)
                parts.Add(gender);
            return string.Join(" · ", parts);
        }
    }

    public string Tooltip => $"{Title}\n{FileName}\n{DetailsLabel}" + (Creator is null ? "" : "\n" + L.F("Ersteller: {0}", Creator)) +
                             (IsEnabled ? "" : "\n" + L.T("(deaktiviert)"));

    /// <summary>Loaded in the background when the tile first becomes visible.</summary>
    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnail is null && !_loading && _thumbnailPath is not null)
            {
                _loading = true;
                _ = LoadAsync();
            }
            return _thumbnail;
        }
        private set => SetProperty(ref _thumbnail, value);
    }

    private async Task LoadAsync() => Thumbnail = await ThumbnailLoader.LoadAsync(_thumbnailPath!);

    /// <summary>Age filter index as in <see cref="CatalogViewModel.AgeOptions"/> (1 = toddler … 5 = elder).</summary>
    public bool HasAge(int index)
    {
        if (Info.AgeGender == 0)
            return false;
        uint mask = index switch
        {
            1 => Core.Catalog.CasPartInfo.AgeToddler,
            2 => Core.Catalog.CasPartInfo.AgeChild,
            3 => Core.Catalog.CasPartInfo.AgeTeen,
            4 => Core.Catalog.CasPartInfo.AgeYoungAdult | Core.Catalog.CasPartInfo.AgeAdult,
            5 => Core.Catalog.CasPartInfo.AgeElder,
            _ => uint.MaxValue
        };
        return (Info.AgeGender & mask) != 0;
    }

    public bool HasGender(bool female) =>
        (Info.AgeGender & (female ? Core.Catalog.CasPartInfo.GenderFemale : Core.Catalog.CasPartInfo.GenderMale)) != 0;
}
