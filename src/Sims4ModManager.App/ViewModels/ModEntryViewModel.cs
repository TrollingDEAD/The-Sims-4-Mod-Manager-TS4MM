using CommunityToolkit.Mvvm.ComponentModel;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// UI wrapper around a scanned <see cref="ModEntry"/>. Setting <see cref="IsEnabled"/> from the
/// UI raises a toggle request instead of mutating the model directly, since actually flipping a
/// mod's state means renaming files on disk and re-scanning.
/// </summary>
public partial class ModEntryViewModel : ObservableObject
{
    private readonly Action<ModEntryViewModel, bool> _onToggleRequested;
    private bool _suppressToggleRequest;

    public ModEntry Model { get; }

    [ObservableProperty]
    private bool isEnabled;

    /// <summary>Number of library (Tray) items that use CC from this mod.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayUsageLabel))]
    private int trayUsage;

    public string TrayUsageLabel => TrayUsage == 0 ? "–" : TrayUsage.ToString();

    /// <summary>Set when the mod is part of a conflict group; shown as tooltip of the warning icon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    private string? conflictTooltip;

    public bool HasConflict => ConflictTooltip is not null;

    /// <summary>From the catalog (filled in once it is built).</summary>
    [ObservableProperty] private string categoryLabel = string.Empty;
    [ObservableProperty] private string creatorLabel = string.Empty;

    /// <summary>The user's note (tooltip of the note icon) and tags.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string? noteTooltip;

    public bool HasNote => NoteTooltip is not null;

    [ObservableProperty]
    private bool isFavorite;

    /// <summary>What of the game this mod replaces (tooltip of the icon in the list); null if nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplacesGame))]
    private string? gameReplacementTooltip;

    public bool ReplacesGame => GameReplacementTooltip is not null;

    public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();

    public ModEntryViewModel(ModEntry model, Action<ModEntryViewModel, bool> onToggleRequested)
    {
        Model = model;
        _onToggleRequested = onToggleRequested;

        _suppressToggleRequest = true;
        IsEnabled = model.IsEnabled;
        _suppressToggleRequest = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressToggleRequest)
            return;

        _onToggleRequested(this, value);
    }

    public void SetNote(ModNote? note)
    {
        Tags = note?.Tags ?? (IReadOnlyList<string>)Array.Empty<string>();
        IsFavorite = note?.IsFavorite ?? false;
        if (note is null || (string.IsNullOrWhiteSpace(note.Note) && string.IsNullOrWhiteSpace(note.Reason)
            && note.Tags.Count == 0 && string.IsNullOrWhiteSpace(note.DownloadUrl)))
        {
            NoteTooltip = null;
            return;
        }
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note.Note)) lines.Add(note.Note);
        if (!string.IsNullOrWhiteSpace(note.Reason)) lines.Add(L.F("Warum installiert: {0}", note.Reason));
        if (note.Tags.Count > 0) lines.Add(L.F("Tags: {0}", string.Join(", ", note.Tags)));
        if (!string.IsNullOrWhiteSpace(note.DownloadUrl)) lines.Add(note.DownloadUrl);
        NoteTooltip = string.Join("\n", lines);
    }

    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public bool IsPartiallyEnabled => Model.IsPartiallyEnabled;

    public string ContentLabel => (Model.ContainsPackage, Model.ContainsScript) switch
    {
        (true, true) => L.T("Package + Skript"),
        (true, false) => L.T("Package"),
        (false, true) => L.T("Skript"),
        _ => "-"
    };

    /// <summary>Tooltip of the "Art" column: file vs. folder, what it contains and where it lives.</summary>
    public string KindTooltip => (Model.IsFolder
        ? L.F("Ordner mit {0} Datei(en) – {1}", FileCount, ContentLabel)
        : L.F("Einzelne Datei – {0}", ContentLabel))
        + (Model.Collection.Length > 0 ? "\n" + L.F("Im Sammelordner „{0}“", Model.Collection) : "");

    public string SizeLabel => Formatting.Size(Model.TotalSizeBytes);
    public string LastModifiedLabel => Model.LastModifiedUtc.ToLocalTime().ToString("d", L.Culture);
    public string LastModifiedTooltip => L.F("Zuletzt geändert: {0}", Model.LastModifiedUtc.ToLocalTime().ToString("g", L.Culture));
    public int FileCount => Model.Files.Count;
}
