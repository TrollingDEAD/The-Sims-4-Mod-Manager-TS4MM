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

    /// <summary>
    /// "Always enable together" group name. Every mod sharing the same (case-insensitive) group
    /// name toggles along with the others whenever one of them is enabled or disabled.
    /// </summary>
    public string? Group { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Note) && Tags.Count == 0 && string.IsNullOrWhiteSpace(DownloadUrl)
                           && string.IsNullOrWhiteSpace(CreatorUrl) && string.IsNullOrWhiteSpace(Reason) && !IsFavorite
                           && string.IsNullOrWhiteSpace(Group);
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
        note.Group = string.IsNullOrWhiteSpace(note.Group) ? null : note.Group.Trim();
        if (note.IsEmpty)
            Notes.Remove(modId);
        else
            Notes[modId] = note;
        JsonFile.WriteAtomic(_path, new NotesFile { Notes = Notes });
    }

    /// <summary>
    /// Every mod id (from <paramref name="candidateIds"/>) whose note has the same "always enable
    /// together" group as <paramref name="group"/> - the mods that should toggle alongside it.
    /// </summary>
    public IReadOnlyList<string> ModIdsInGroup(string group, IEnumerable<string> candidateIds) =>
        candidateIds.Where(id => string.Equals(Get(id)?.Group, group, StringComparison.CurrentCultureIgnoreCase)).ToList();

    /// <summary>Flips the favorite flag for a mod, preserving its other note fields, and returns the new state.</summary>
    public bool ToggleFavorite(string modId)
    {
        var note = Get(modId) ?? new ModNote();
        note.IsFavorite = !note.IsFavorite;
        Set(modId, note);
        return note.IsFavorite;
    }

    /// <summary>
    /// Moves a note from <paramref name="oldId"/> to <paramref name="newId"/> - a mod's ID is just its
    /// file/folder name, so renaming one (e.g. a load-order prefix change) would otherwise silently
    /// orphan its note/tags/favorite/group. No-op if there is nothing to move, or if
    /// <paramref name="newId"/> already has its own note (never overwrites existing data).
    /// </summary>
    public void Rekey(string oldId, string newId)
    {
        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            return;
        if (!Notes.TryGetValue(oldId, out var note) || Notes.ContainsKey(newId))
            return;
        Notes.Remove(oldId);
        Notes[newId] = note;
        JsonFile.WriteAtomic(_path, new NotesFile { Notes = Notes });
    }
}
