using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.Core.Catalog;

/// <summary>The user's own notes about a mod.</summary>
public sealed class ModNote
{
    public string? Note { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? DownloadUrl { get; set; }
    public string? CreatorUrl { get; set; }

    /// <summary>"Warum installiert?"</summary>
    public string? Reason { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Note) && Tags.Count == 0 && string.IsNullOrWhiteSpace(DownloadUrl)
                           && string.IsNullOrWhiteSpace(CreatorUrl) && string.IsNullOrWhiteSpace(Reason) && !IsFavorite;
}

/// <summary>
/// Notes, tags and links per mod in %AppData%\Sims4ModManager\notes.json - outside the Mods folder and
/// keyed by the mod ID (its file or folder name), so they survive toggling and sorting.
/// </summary>
public sealed class ModNotesStore
{
    private sealed class NotesFile
    {
        public Dictionary<string, ModNote> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly string _path;
    private Dictionary<string, ModNote>? _notes;

    public ModNotesStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.Root, "notes.json");
    }

    private Dictionary<string, ModNote> Notes =>
        _notes ??= new Dictionary<string, ModNote>(JsonFile.TryRead<NotesFile>(_path)?.Notes ?? new(), StringComparer.OrdinalIgnoreCase);

    public ModNote? Get(string modId) => Notes.TryGetValue(modId, out var note) ? note : null;

    /// <summary>All notes, keyed by mod ID; for bundling into a portable settings backup.</summary>
    public IReadOnlyDictionary<string, ModNote> AllNotes => Notes;

    public IReadOnlyCollection<string> AllTags =>
        Notes.Values.SelectMany(n => n.Tags).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Stores (or, when empty, removes) the note. Throws on write errors.</summary>
    public void Set(string modId, ModNote note)
    {
        note.Tags = note.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (note.IsEmpty)
            Notes.Remove(modId);
        else
            Notes[modId] = note;
        JsonFile.WriteAtomic(_path, new NotesFile { Notes = Notes });
    }

    /// <summary>Flips the favorite flag for a mod, preserving its other note fields, and returns the new state.</summary>
    public bool ToggleFavorite(string modId)
    {
        var note = Get(modId) ?? new ModNote();
        note.IsFavorite = !note.IsFavorite;
        Set(modId, note);
        return note.IsFavorite;
    }
}
