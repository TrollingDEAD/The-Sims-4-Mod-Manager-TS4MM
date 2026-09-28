using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Game;
using Sims4ModManager.Core.Health;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// The "Spiel" tab: the installed game and its packs, and what the mods do to it - default
/// replacements, tuning overrides, recolors whose mesh is missing. Also hands the game index to the
/// library (required packs, missing CC) and its findings to the overview.
/// </summary>
public partial class GameViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsStore _settings = new();
    private int _loadVersion, _analyzeVersion;
    private IReadOnlyList<GameReplacement> _replacements = Array.Empty<GameReplacement>();

    public GameViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;
        GameLauncher.PreferredInstallFolder = _settings.Load().GameInstallPath;
    }

    public static string CachePath => Path.Combine(AppPaths.Cache, "gameindex.bin");

    public GameIndex? Index { get; private set; }

    /// <summary>Raised when the index is (re)loaded, so the library can check its items.</summary>
    public event Action? IndexReady;

    public ObservableCollection<string> Packs { get; } = new();
    public ObservableCollection<GameReplacementViewModel> Replacements { get; } = new();
    public ObservableCollection<MissingMeshViewModel> MissingMeshes { get; } = new();

    /// <summary>Findings for the overview page.</summary>
    public IReadOnlyList<HealthIssue> HealthIssues { get; private set; } = Array.Empty<HealthIssue>();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyText = string.Empty;
    [ObservableProperty] private string installLabel = "–";
    [ObservableProperty] private string indexLabel = "–";
    [ObservableProperty] private string packsHeader = L.T("Installierte Packs");
    [ObservableProperty] private string replacementsHeader = L.T("Mods, die Spielinhalte ersetzen");
    [ObservableProperty] private string missingHeader = L.T("Recolors ohne Mesh");
    [ObservableProperty] private bool onlyTuning;
    [ObservableProperty] private bool hasIndex;
    [ObservableProperty] private bool hasMissingMeshes;
    [ObservableProperty] private bool hasReplacements;

    partial void OnOnlyTuningChanged(bool value) => ShowReplacements();

    public string? InstallFolder => GameLauncher.FindInstallFolder();

    /// <summary>Loads the index (from the cache when the game is unchanged), then analyzes the mods.</summary>
    public async Task RefreshAsync(bool rebuild = false)
    {
        int version = ++_loadVersion;
        string? folder = InstallFolder;
        InstallLabel = folder ?? L.T("nicht gefunden – bitte den Spielordner auswählen");
        if (folder is null)
        {
            Index = null;
            HasIndex = false;
            IndexLabel = L.T("Ohne Spielinstallation lassen sich Default Replacements, fehlende Meshes und benötigte Packs nicht prüfen.");
            return;
        }

        IsBusy = true;
        BusyText = L.T("Spielindex wird geladen …");
        try
        {
            if (rebuild)
                TryDelete(CachePath);
            var progress = new Progress<double>(p => BusyText = L.F("Spielindex wird aufgebaut … {0:P0}", p));
            var index = await Task.Run(() => GameIndex.LoadOrBuild(folder, CachePath, progress));
            if (version == _loadVersion)
                Index = index;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Index = null;
            IndexLabel = L.F("Spielindex konnte nicht erstellt werden: {0}", ex.Message);
        }
        finally
        {
            if (version == _loadVersion)
                IsBusy = false;
        }
        if (version != _loadVersion)
            return;

        HasIndex = Index is not null;
        if (Index is not null)
        {
            IndexLabel = L.F("{0} Spiel-Packages, {1:N0} Ressourcen, {2:N0} Katalog-Einträge – Stand {3:g}",
                Index.PackageCount, Index.KeyCount, Index.CatalogItemCount, Index.BuiltUtc.ToLocalTime());
            Packs.Clear();
            foreach (var pack in Index.Packs.Skip(1))
                Packs.Add(pack.DisplayName);
            PacksHeader = L.F("Installierte Packs ({0})", Packs.Count);
            IndexReady?.Invoke();
        }
        await AnalyzeModsAsync();
    }

    /// <summary>Re-checks the mods against the loaded index (after every rescan and catalog update).</summary>
    public async Task AnalyzeModsAsync()
    {
        var index = Index;
        if (index is null)
            return;
        int version = ++_analyzeVersion;
        var mods = _main.CurrentMods;
        var catalog = _main.Catalog;
        IsBusy = true;
        BusyText = L.T("Mods werden mit dem Spiel verglichen …");
        try
        {
            var (replacements, missing) = await Task.Run(() => (
                GameContentAnalyzer.FindReplacements(mods, index),
                GameContentAnalyzer.FindMissingMeshes(mods, catalog.InfoOf, index)));
            if (version != _analyzeVersion)
                return;

            _replacements = replacements;
            ShowReplacements();
            MissingMeshes.Clear();
            foreach (var mesh in missing)
                MissingMeshes.Add(new MissingMeshViewModel(mesh));
            HasMissingMeshes = MissingMeshes.Count > 0;
            MissingHeader = L.F("Recolors ohne Mesh ({0})", MissingMeshes.Count);
            HealthIssues = BuildHealthIssues(replacements, missing);
            _main.Health.ShowGameIssues();
            _main.ApplyGameReplacements(replacements);
        }
        finally
        {
            if (version == _analyzeVersion)
                IsBusy = false;
        }
    }

    private void ShowReplacements()
    {
        var updated = GameUpdatedUtc();
        Replacements.Clear();
        foreach (var replacement in _replacements.Where(r => !OnlyTuning || r.TouchesTuning))
            Replacements.Add(new GameReplacementViewModel(replacement, updated));
        HasReplacements = Replacements.Count > 0;
        int tuning = _replacements.Count(r => r.TouchesTuning);
        ReplacementsHeader = L.F("Mods, die Spielinhalte ersetzen ({0}, davon {1} mit Tuning)", _replacements.Count, tuning);
    }

    /// <summary>When the game was last updated (from GameVersion.txt), for "older than the update" warnings.</summary>
    private DateTime? GameUpdatedUtc()
    {
        string? modsPath = _main.ModsPath;
        string? dataFolder = modsPath is null ? null : ModsFolderLocator.TryGetGameDataFolder(modsPath)?.Path;
        return dataFolder is null ? null : GameInfo.TryGetVersionTimestampUtc(dataFolder);
    }

    private List<HealthIssue> BuildHealthIssues(IReadOnlyList<GameReplacement> replacements, IReadOnlyList<MissingMesh> missing)
    {
        var issues = new List<HealthIssue>();
        var active = missing.Where(m => m.File.IsEnabled).ToList();
        if (active.Count > 0)
            issues.Add(new HealthIssue
            {
                Id = "missing-mesh",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Warning,
                Title = L.T("Recolors ohne Mesh"),
                Description = L.F("{0} Recolor(s) verweisen auf ein Mesh, das weder ein Mod noch das Spiel enthält – im Spiel sind sie unsichtbar oder fehlen. ", active.Count) +
                              L.T("Das passende Mesh beim Ersteller herunterladen (Details im Tab „Spiel“)."),
                Paths = active.Select(m => m.File.AbsolutePath).Distinct().ToList()
            });

        var updated = GameUpdatedUtc();
        var staleTuning = replacements.Where(r => r.TouchesTuning && r.File.IsEnabled && updated is { } u && r.File.LastWriteUtc < u).ToList();
        if (staleTuning.Count > 0)
            issues.Add(new HealthIssue
            {
                Id = "stale-tuning-override",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Warning,
                Title = L.T("Tuning-Overrides älter als das Spiel-Update"),
                Description = L.F("{0} Mod(s) überschreiben Spiel-Tuning und sind älter als das letzte Update. Solche Mods verursachen nach Patches am häufigsten Fehler – auf Updates prüfen.", staleTuning.Count),
                Paths = staleTuning.Select(r => r.File.AbsolutePath).ToList()
            });

        int defaults = replacements.Count(r => !r.TouchesTuning && r.File.IsEnabled);
        if (defaults > 0)
            issues.Add(new HealthIssue
            {
                Id = "default-replacements",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Info,
                Title = L.T("Default Replacements"),
                Description = L.F("{0} Mod-Datei(en) ersetzen Inhalte des Spiels (Texturen, Meshes, CAS-Teile, Slider). Von zwei Mods, die dasselbe ersetzen, wirkt nur einer.", defaults),
                Paths = replacements.Where(r => !r.TouchesTuning && r.File.IsEnabled).Select(r => r.File.AbsolutePath).ToList()
            });
        return issues;
    }

    public IReadOnlyList<GameReplacement> ReplacementsOf(ModEntry mod) => _replacements.Where(r => r.Mod == mod).ToList();

    [RelayCommand]
    private async Task ChooseInstallFolderAsync()
    {
        string? folder = _dialogs.PickFolder(L.T("Sims 4-Installationsordner auswählen (enthält „Data“ und „Game“)"));
        if (folder is null)
            return;
        if (!Directory.Exists(Path.Combine(folder, "Data")))
        {
            await _dialogs.ShowAsync(L.T("Spielordner"), L.F("In {0} gibt es keinen Ordner „Data“ – das ist nicht der Installationsordner von Die Sims 4.", folder));
            return;
        }
        _settings.TryUpdate(s => s.GameInstallPath = folder);
        GameLauncher.PreferredInstallFolder = folder;
        await RefreshAsync();
    }

    [RelayCommand]
    private Task RebuildIndexAsync() => RefreshAsync(rebuild: true);

    [RelayCommand]
    private void OpenInstallFolder()
    {
        if (InstallFolder is { } folder)
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ShowReplacementMod(GameReplacementViewModel? vm)
    {
        if (vm is not null)
            _main.ShowMod(vm.Model.Mod);
    }

    [RelayCommand]
    private void ShowMissingMeshMod(MissingMeshViewModel? vm)
    {
        if (vm is not null)
            _main.ShowMod(vm.Model.Mod);
    }

    [RelayCommand]
    private void SearchMesh(MissingMeshViewModel? vm)
    {
        if (vm is null)
            return;
        string query = Uri.EscapeDataString($"sims 4 {vm.SearchTerm} mesh");
        Process.Start(new ProcessStartInfo($"https://www.google.com/search?q={query}") { UseShellExecute = true });
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* rebuilt anyway when stale */ }
    }
}

public sealed class GameReplacementViewModel
{
    public GameReplacementViewModel(GameReplacement replacement, DateTime? gameUpdatedUtc)
    {
        Model = replacement;
        // Folder mods can be huge collections ("CC", "Mods"): the file says more than the folder.
        ModName = replacement.Mod.IsFolder ? FileTitle(replacement.File) : replacement.Mod.DisplayName;
        FileLabel = replacement.Mod.IsFolder ? L.F("in „{0}“", replacement.Mod.DisplayName) : string.Empty;
        Summary = replacement.Summary;
        TuningLabel = replacement.TuningNames.Count == 0 ? string.Empty
            : L.T("Tuning: ") + string.Join(", ", replacement.TuningNames.Take(4)) + (replacement.TuningNames.Count > 4 ? " …" : "");
        IsEnabled = replacement.File.IsEnabled;
        IsOutdated = replacement.TouchesTuning && gameUpdatedUtc is { } updated && replacement.File.LastWriteUtc < updated;
        StatusLabel = !IsEnabled ? L.T("Deaktiviert") : IsOutdated ? L.T("Älter als das Spiel-Update") : string.Empty;
    }

    public GameReplacement Model { get; }
    public string ModName { get; }
    public string FileLabel { get; }
    public string Summary { get; }
    public string TuningLabel { get; }
    public bool IsEnabled { get; }
    public bool IsOutdated { get; }
    public string StatusLabel { get; }
    public bool TouchesTuning => Model.TouchesTuning;

    internal static string FileTitle(ModFileInfo file) =>
        Path.GetFileNameWithoutExtension(Core.ModFileNaming.ToEnabledPath(Path.GetFileName(file.AbsolutePath)));
}

public sealed class MissingMeshViewModel
{
    public MissingMeshViewModel(MissingMesh mesh)
    {
        Model = mesh;
        ModName = mesh.Mod.IsFolder ? GameReplacementViewModel.FileTitle(mesh.File) : mesh.Mod.DisplayName;
        FileLabel = mesh.Mod.IsFolder ? L.F("in „{0}“", mesh.Mod.DisplayName) : string.Empty;
        Item = mesh.Item.Length > 0 ? mesh.Item : L.T("(ohne Namen)");
        MeshLabel = mesh.Mesh.ToString();
        StatusLabel = mesh.Status == MeshStatus.Disabled
            ? L.F("Mesh ist in einem deaktivierten Mod: {0}", mesh.DisabledProvider?.DisplayName)
            : L.T("Mesh fehlt – weder in den Mods noch im Spiel");
        // S4S names clones "Creator_Mesh-Creator_itemName_…": the part before the date usually names the mesh.
        SearchTerm = string.Join(" ", Item.Split('_', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.All(char.IsDigit)).Take(3));
    }

    public MissingMesh Model { get; }
    public string ModName { get; }
    public string FileLabel { get; }
    public string Item { get; }
    public string MeshLabel { get; }
    public string StatusLabel { get; }
    public string SearchTerm { get; }
}
