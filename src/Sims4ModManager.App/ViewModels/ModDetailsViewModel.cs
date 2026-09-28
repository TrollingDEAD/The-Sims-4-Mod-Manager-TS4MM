using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.App.ViewModels;

/// <summary>Details of the selected mod in the "Mods" tab: preview, category, creator, files and the user's notes.</summary>
public sealed partial class ModDetailsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly ModNotesStore _notes;
    private bool _loading;

    public ModDetailsViewModel(MainViewModel main, ModNotesStore notes, ModEntryViewModel mod)
    {
        _main = main;
        _notes = notes;
        Mod = mod;

        var note = notes.Get(mod.Id) ?? new ModNote();
        _loading = true;
        Note = note.Note ?? string.Empty;
        Tags = string.Join(", ", note.Tags);
        DownloadUrl = note.DownloadUrl ?? string.Empty;
        CreatorUrl = note.CreatorUrl ?? string.Empty;
        Reason = note.Reason ?? string.Empty;
        _loading = false;
        selectedPackage = PackageFiles.FirstOrDefault();

        string? thumb = main.Catalog.ThumbnailOf(mod.Model);
        if (thumb is not null)
            _ = LoadThumbnailAsync(thumb);
    }

    public ModEntryViewModel Mod { get; }

    public string Title => Mod.DisplayName;
    public string CategoryLabel => _main.Catalog.CategoryLabelOf(Mod.Model);
    public string CreatorLabel => _main.Catalog.CreatorOf(Mod.Model) ?? L.T("unbekannt");
    public string LocationLabel => Mod.Model.Collection.Length == 0
        ? L.T("direkt im Mods-Ordner")
        : L.F("Sammelordner „{0}“", Mod.Model.Collection);

    public string CatalogLabel
    {
        get
        {
            var infos = Mod.Model.Files.Select(_main.Catalog.InfoOf).OfType<PackageCatalogInfo>().ToList();
            var parts = new List<string>();
            string? name = infos.Select(i => i.Name).FirstOrDefault(n => n is not null);
            if (name is not null && !string.Equals(name, Mod.DisplayName, StringComparison.CurrentCultureIgnoreCase))
                parts.Add(L.F("Im Spiel: „{0}“", name));
            int items = infos.Sum(i => i.ItemCount);
            if (items > 1)
                parts.Add(L.F("{0} Varianten", items));
            uint ages = infos.Aggregate(0u, (acc, i) => acc | i.AgeGender);
            if (ages != 0)
                parts.Add($"{ContentCategories.AgeLabel(ages)} · {ContentCategories.GenderLabel(ages)}");
            return string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<string> Files => Mod.Model.Files
        .Select(f => $"{f.RelativePathInMod}  ({Formatting.Size(f.SizeBytes)}{(f.IsEnabled ? "" : ", " + L.T("deaktiviert"))})")
        .ToList();

    public bool CanMakeCollection => Mod.Model.IsFolder && !Mod.Model.ContainsScript;

    /// <summary>What of the game this mod replaces (from the game index), empty if nothing.</summary>
    public string GameLabel => string.Join(Environment.NewLine, _main.Game.ReplacementsOf(Mod.Model)
        .Select(r => (Mod.Model.IsFolder ? r.File.RelativePathInMod + ": " : "") + r.Summary +
                     (r.TuningNames.Count > 0 ? " (" + string.Join(", ", r.TuningNames.Take(3)) + ")" : "")));

    public bool HasGameLabel => GameLabel.Length > 0;

    // --- Package contents ("Inhalt") -----------------------------------------------------------

    public IReadOnlyList<ModFileInfo> PackageFiles => Mod.Model.Files.Where(f => f.Kind == ModFileKind.Package).ToList();
    public bool HasPackages => PackageFiles.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResourceTypes), nameof(ContentSummary), nameof(CanOptimize), nameof(CanUnmerge), nameof(OptimizeHint))]
    private ModFileInfo? selectedPackage;

    private PackageInspection? Inspection => SelectedPackage is null ? null : PackageTools.Inspect(SelectedPackage.Resources);

    /// <summary>Resources grouped by type, largest first.</summary>
    public IReadOnlyList<ResourceTypeRow> ResourceTypes => SelectedPackage?.Resources
        .GroupBy(r => r.Key.Type)
        .Select(g => new ResourceTypeRow(ResourceTypeCatalog.Describe(g.Key).Name, $"{g.Key:X8}", g.Count(),
            Formatting.Size(g.Sum(r => (long)r.StoredSize)), Formatting.Size(g.Sum(r => (long)r.MemorySize)),
            g.Count(r => r.CompressionType == DbpfReader.CompressionNone), g.Sum(r => (long)r.StoredSize)))
        .OrderByDescending(r => r.Bytes)
        .ToList() ?? new List<ResourceTypeRow>();

    public string ContentSummary
    {
        get
        {
            if (SelectedPackage is null || Inspection is not { } i)
                return string.Empty;
            if (SelectedPackage.IsUnreadable)
                return L.T("Das Package ist nicht lesbar (beschädigt oder kein Sims 4-Package).");
            var parts = new List<string> { L.F("{0} Ressourcen, {1}", i.ResourceCount, Formatting.Size(SelectedPackage.SizeBytes)) };
            if (i.IsEmpty)
                parts.Add(L.T("leer – das Package bewirkt im Spiel nichts"));
            if (CanUnmerge)
                parts.Add(L.F("zusammengeführt aus {0} Dateien", PackageMerger.TryReadManifest(SelectedPackage.AbsolutePath)?.Count ?? 0));
            if (i.DuplicateEntries > 0)
                parts.Add(L.F("{0} doppelte Einträge", i.DuplicateEntries));
            if (i.UncompressedCount > 0)
                parts.Add(L.F("{0} unkomprimiert ({1})", i.UncompressedCount, Formatting.Size(i.UncompressedBytes)));
            return string.Join(" · ", parts);
        }
    }

    public bool CanOptimize => Inspection?.CanOptimize == true && SelectedPackage?.IsEnabled == true;
    public bool CanUnmerge => SelectedPackage is not null && PackageMerger.IsMerged(SelectedPackage.Resources);

    public string OptimizeHint => Inspection is { CanOptimize: false } ? L.T("Nichts zu optimieren.") : string.Empty;

    [RelayCommand]
    private Task OptimizeAsync() => SelectedPackage is { } file ? _main.OptimizePackageAsync(file) : Task.CompletedTask;

    [RelayCommand]
    private Task UnmergeAsync() => SelectedPackage is { } file ? _main.UnmergePackageAsync(file) : Task.CompletedTask;

    [ObservableProperty] private BitmapSource? thumbnail;
    [ObservableProperty] private string note = string.Empty;
    [ObservableProperty] private string tags = string.Empty;
    [ObservableProperty] private string downloadUrl = string.Empty;
    [ObservableProperty] private string creatorUrl = string.Empty;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private bool isDirty;

    partial void OnNoteChanged(string value) => MarkDirty();
    partial void OnTagsChanged(string value) => MarkDirty();
    partial void OnDownloadUrlChanged(string value) => MarkDirty();
    partial void OnCreatorUrlChanged(string value) => MarkDirty();
    partial void OnReasonChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (!_loading)
            IsDirty = true;
    }

    private async Task LoadThumbnailAsync(string path) => Thumbnail = await ThumbnailLoader.LoadAsync(path);

    [RelayCommand]
    private void SaveNotes()
    {
        var note = new ModNote
        {
            Note = Note.Trim(),
            Tags = Tags.Split(',', ';').Select(t => t.Trim()).ToList(),
            DownloadUrl = DownloadUrl.Trim(),
            CreatorUrl = CreatorUrl.Trim(),
            Reason = Reason.Trim()
        };
        try
        {
            _notes.Set(Mod.Id, note);
            IsDirty = false;
            Mod.SetNote(note);
            _main.StatusMessage = L.F("Notizen zu „{0}“ gespeichert.", Mod.DisplayName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.StatusMessage = L.F("Notizen konnten nicht gespeichert werden: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        else
            _main.StatusMessage = L.T("Bitte einen vollständigen Link (https://…) eingeben.");
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        string path = Mod.Model.AbsolutePath;
        Process.Start("explorer.exe", Mod.Model.IsFolder ? $"\"{path}\"" : $"/select,\"{path}\"");
    }

    [RelayCommand]
    private Task MakeCollectionAsync() => _main.MakeCollectionAsync(Mod.Model);
}

/// <summary>Size and date formatting shared by the view models.</summary>
public static class Formatting
{
    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return string.Format(L.Culture, "{0:0.#} {1}", size, units[unit]);
    }
}

/// <summary>One line of the package content view: a resource type with count and sizes.</summary>
public sealed record ResourceTypeRow(string Name, string TypeId, int Count, string StoredLabel, string MemoryLabel, int Uncompressed, long Bytes);
