using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// "Zusammenführen": several CC mods become one package (with an S4S-compatible file list, so it can
/// be split again). Script mods are left out - their packages belong to the script.
/// </summary>
public partial class MergeViewModel : ObservableObject
{
    public const string TargetFolderName = "Zusammengeführt";

    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private readonly List<MergeCandidateViewModel> _all;

    public MergeViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;
        _all = main.CurrentMods
            .Where(m => !m.ContainsScript && m.Files.Any(f => f.Kind == ModFileKind.Package && f.IsEnabled))
            .Select(m => new MergeCandidateViewModel(m, main.Catalog.CategoryLabelOf(m), main.Catalog.CreatorOf(m) ?? string.Empty, OnSelectionChanged))
            .OrderBy(c => c.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Categories = new[] { L.T("Alle Kategorien") }.Concat(_all.Select(c => c.Category).Where(c => c.Length > 0).Distinct().Order()).ToList();
        selectedCategory = Categories[0];
        fileName = L.F("Zusammengeführt {0:yyyy-MM-dd}", DateTime.Now);
        Refresh();
        OnSelectionChanged();
    }

    public ObservableCollection<MergeCandidateViewModel> Visible { get; } = new();
    public IReadOnlyList<string> Categories { get; }

    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private string selectedCategory;
    [ObservableProperty] private string fileName;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private string warning = string.Empty;
    [ObservableProperty] private bool canMerge;
    [ObservableProperty] private bool isBusy;

    public event Action<bool>? CloseRequested;

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnSelectedCategoryChanged(string value) => Refresh();
    partial void OnFileNameChanged(string value) => OnSelectionChanged();

    private void Refresh()
    {
        string query = Search.Trim();
        bool all = SelectedCategory == Categories[0];
        Visible.Clear();
        foreach (var c in _all.Where(c => (all || c.Category == SelectedCategory)
                                          && (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                              || c.Creator.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
            Visible.Add(c);
    }

    private List<ModFileInfo> SelectedFiles() => _all.Where(c => c.IsSelected)
        .SelectMany(c => c.Model.Files.Where(f => f.Kind == ModFileKind.Package && f.IsEnabled))
        .OrderBy(f => f.AbsolutePath, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private void OnSelectionChanged()
    {
        var files = SelectedFiles();
        long bytes = files.Sum(f => f.SizeBytes);
        int overlapping = files.SelectMany(f => f.Resources.Select(r => r.Key).Where(k => k.Type != PackageMerger.ManifestType).Distinct())
            .GroupBy(k => k).Count(g => g.Count() > 1);
        int mods = _all.Count(c => c.IsSelected);
        Summary = mods == 0
            ? L.T("Mods auswählen, die zu einem Package zusammengeführt werden sollen.")
            : L.F("{0} Mods mit {1} Dateien ({2}) → „{3}\\{4}.package“", mods, files.Count, Formatting.Size(bytes), TargetFolderName, SafeName);

        var warnings = new List<string>();
        if (bytes > PackageMerger.RecommendedMaxBytes)
            warnings.Add(L.T("Größer als 1 GB – die Community rät zu kleineren Paketen (lieber mehrere Merges nach Kategorie)."));
        if (files.Count > PackageMerger.RecommendedMaxFiles)
            warnings.Add(L.F("Mehr als {0} Dateien in einem Package – lieber aufteilen.", PackageMerger.RecommendedMaxFiles));
        if (overlapping > 0)
            warnings.Add(L.F("{0} Ressourcen kommen in mehreren Dateien vor – im Merge bleibt jeweils die aus der alphabetisch ersten Datei.", overlapping));
        Warning = string.Join(Environment.NewLine, warnings);
        CanMerge = files.Count >= 2 && SafeName.Length > 0 && bytes < DbpfWriter.MaxPackageBytes && !File.Exists(TargetPath);
        if (File.Exists(TargetPath))
            Warning = (Warning.Length > 0 ? Warning + Environment.NewLine : "") + L.T("Eine Datei mit diesem Namen gibt es schon.");
    }

    private string SafeName => new string(FileName.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).TrimEnd('.');

    private string TargetPath => Path.Combine(_main.ModsPath ?? string.Empty, TargetFolderName, SafeName + ModFileNaming.PackageExtension);

    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var c in Visible)
            c.SetSelected(true);
        OnSelectionChanged();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var c in _all)
            c.SetSelected(false);
        OnSelectionChanged();
    }

    [RelayCommand]
    private async Task MergeAsync()
    {
        var files = SelectedFiles();
        if (files.Count < 2 || _main.ModsPath is null || !await _main.EnsureGameClosedAsync())
            return;
        var mods = _all.Where(c => c.IsSelected).Select(c => c.Model).ToList();
        string target = TargetPath;
        string modsRoot = _main.ModsPath;
        IsBusy = true;
        MergeResult? result = null;
        string? error = null;
        try
        {
            await Task.Run(() =>
            {
                using var recorder = _main.Journal.Begin(L.F("Zusammengeführt: {0} ({1} Dateien)", Path.GetFileName(target), files.Count));
                recorder.Create(target, temp => result = PackageMerger.Merge(files.Select(f => f.AbsolutePath).ToList(), temp));
                foreach (var file in files)
                    recorder.Delete(file.AbsolutePath);
                foreach (var mod in mods.Where(m => m.IsFolder))
                    RemoveEmptyFolders(recorder, mod.AbsolutePath, modsRoot);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null)
        {
            await _dialogs.ShowAsync(L.T("Zusammenführen"), L.F("Zusammenführen fehlgeschlagen: {0}", error));
            return;
        }
        _main.AfterChange();
        _main.StatusMessage = L.F("{0} Dateien zu „{1}“ zusammengeführt ({2}).", files.Count, Path.GetFileName(target), Formatting.Size(result?.Bytes ?? 0)) +
                              " " + L.T("Zerlegen: in den Details des neuen Packages.") + " " + MainViewModel.UndoHint;
        CloseRequested?.Invoke(true);
    }

    /// <summary>Folders emptied by the merge are removed (deepest first), never the Mods folder itself.</summary>
    private static void RemoveEmptyFolders(Core.Backup.ChangeRecorder recorder, string folder, string modsRoot)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (string sub in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            recorder.DeleteEmptyDirectory(sub);
        if (!string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetFullPath(modsRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            recorder.DeleteEmptyDirectory(folder);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}

public sealed partial class MergeCandidateViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _silent;

    public MergeCandidateViewModel(ModEntry model, string category, string creator, Action changed)
    {
        Model = model;
        Category = category;
        Creator = creator;
        _changed = changed;
        var packages = model.Files.Where(f => f.Kind == ModFileKind.Package && f.IsEnabled).ToList();
        Details = L.F("{0} Datei(en), {1}", packages.Count, Formatting.Size(packages.Sum(f => f.SizeBytes)));
    }

    public ModEntry Model { get; }
    public string Name => Model.DisplayName;
    public string Category { get; }
    public string Creator { get; }
    public string Details { get; }

    [ObservableProperty] private bool isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_silent)
            _changed();
    }

    public void SetSelected(bool value)
    {
        _silent = true;
        IsSelected = value;
        _silent = false;
    }
}
