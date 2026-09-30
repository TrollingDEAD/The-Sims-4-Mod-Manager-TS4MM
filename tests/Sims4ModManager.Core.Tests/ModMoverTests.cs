using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Catalog;

namespace Sims4ModManager.Core.Tests;

public class ModMoverTests : IDisposable
{
    private readonly string _root;
    private readonly string _mods;

    public ModMoverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Tests_" + Guid.NewGuid());
        _mods = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(_mods);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void MovesLooseFileIntoNewCollectionAndKeepsTheSameId()
    {
        File.WriteAllBytes(Path.Combine(_mods, "MyHair.package"), new byte[] { 0 });
        var mod = ModScanner.Scan(_mods).Single();

        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        using (var recorder = journal.Begin("move"))
        {
            var result = ModMover.Move(mod, _mods, "CAS - Haare", recorder);
            Assert.True(result.Success);
        }

        Assert.True(File.Exists(Path.Combine(_mods, "CAS - Haare", "MyHair.package")));
        Assert.False(File.Exists(Path.Combine(_mods, "MyHair.package")));

        var moved = ModScanner.Scan(_mods).Single();
        Assert.Equal(mod.Id, moved.Id); // notes/tags/profiles keyed by Id still apply
        Assert.Equal("CAS - Haare", moved.Collection);
    }

    [Fact]
    public void MovesFolderModBackToTheModsRootAndRemovesTheEmptyCollectionFolder()
    {
        string folder = Path.Combine(_mods, "CAS - Haare", "HairSet");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "part1.package"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(folder, "part2.package"), new byte[] { 0 });
        File.WriteAllText(Path.Combine(_mods, "CAS - Haare", ModScanner.CollectionMarker), "marker");

        var mod = ModScanner.Scan(_mods).Single(m => m.DisplayName == "HairSet");
        Assert.Equal("CAS - Haare", mod.Collection);

        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        using (var recorder = journal.Begin("move"))
        {
            var result = ModMover.Move(mod, _mods, "", recorder);
            Assert.True(result.Success);
        }

        Assert.True(Directory.Exists(Path.Combine(_mods, "HairSet")));
        Assert.True(File.Exists(Path.Combine(_mods, "HairSet", "part1.package")));
        Assert.False(Directory.Exists(folder));

        var moved = ModScanner.Scan(_mods).Single(m => m.DisplayName == "HairSet");
        Assert.Equal(mod.Id, moved.Id);
        Assert.Equal("", moved.Collection);
    }

    [Fact]
    public void RefusesToMoveWhenTheTargetAlreadyHasSomethingWithTheSameName()
    {
        File.WriteAllBytes(Path.Combine(_mods, "MyHair.package"), new byte[] { 0 });
        Directory.CreateDirectory(Path.Combine(_mods, "CAS - Haare"));
        File.WriteAllText(Path.Combine(_mods, "CAS - Haare", ModScanner.CollectionMarker), "marker");
        File.WriteAllBytes(Path.Combine(_mods, "CAS - Haare", "MyHair.package"), new byte[] { 1 });

        var mod = ModScanner.Scan(_mods).Single(m => m.Collection == "");

        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        using var recorder = journal.Begin("move");
        var result = ModMover.Move(mod, _mods, "CAS - Haare", recorder);

        Assert.False(result.Success);
        Assert.NotNull(result.Reason);
        Assert.True(File.Exists(Path.Combine(_mods, "MyHair.package"))); // untouched
    }

    [Fact]
    public void RefusesToMoveAScriptModDeeperThanOneFolder()
    {
        File.WriteAllBytes(Path.Combine(_mods, "mc_cmd_center.ts4script"), new byte[] { 0 });
        var mod = ModScanner.Scan(_mods).Single();
        Assert.True(mod.ContainsScript);

        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        using var recorder = journal.Begin("move");
        var result = ModMover.Move(mod, _mods, Path.Combine("Skript-Mods", "Sub"), recorder);

        Assert.False(result.Success);
        Assert.True(File.Exists(Path.Combine(_mods, "mc_cmd_center.ts4script"))); // untouched
    }

    [Fact]
    public void MoveIsUndoable()
    {
        File.WriteAllBytes(Path.Combine(_mods, "MyHair.package"), new byte[] { 0 });
        var mod = ModScanner.Scan(_mods).Single();

        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        string id;
        using (var recorder = journal.Begin("move"))
        {
            Assert.True(ModMover.Move(mod, _mods, "CAS - Haare", recorder).Success);
            id = recorder.Id;
        }

        Assert.True(journal.Undo(id).Success);
        Assert.True(File.Exists(Path.Combine(_mods, "MyHair.package")));
        Assert.False(File.Exists(Path.Combine(_mods, "CAS - Haare", "MyHair.package")));
    }
}
