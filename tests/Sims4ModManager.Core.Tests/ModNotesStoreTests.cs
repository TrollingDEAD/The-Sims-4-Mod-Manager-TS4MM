using Sims4ModManager.Core.Catalog;

namespace Sims4ModManager.Core.Tests;

public class ModNotesStoreTests : IDisposable
{
    private readonly string _path;

    public ModNotesStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "S4MM_Notes_" + Guid.NewGuid() + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void RekeyMovesNoteToNewId()
    {
        var store = new ModNotesStore(_path);
        store.Set("old_name.package", new ModNote { Note = "Keep this", IsFavorite = true });

        store.Rekey("old_name.package", "010_old_name.package");

        Assert.Null(store.Get("old_name.package"));
        var moved = store.Get("010_old_name.package");
        Assert.NotNull(moved);
        Assert.Equal("Keep this", moved!.Note);
        Assert.True(moved.IsFavorite);
    }

    [Fact]
    public void RekeyIsNoOpWhenOldIdHasNoNote()
    {
        var store = new ModNotesStore(_path);
        store.Rekey("nothing_here.package", "010_nothing_here.package");
        Assert.Null(store.Get("010_nothing_here.package"));
    }

    [Fact]
    public void RekeyNeverOverwritesAnExistingNoteAtTheNewId()
    {
        var store = new ModNotesStore(_path);
        store.Set("old.package", new ModNote { Note = "Old note" });
        store.Set("new.package", new ModNote { Note = "Already has its own note" });

        store.Rekey("old.package", "new.package");

        Assert.Equal("Old note", store.Get("old.package")?.Note);
        Assert.Equal("Already has its own note", store.Get("new.package")?.Note);
    }
}
