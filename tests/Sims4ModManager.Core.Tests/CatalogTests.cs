using System.Text;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Catalog;

namespace Sims4ModManager.Core.Tests;

public class CatalogTests : IDisposable
{
    private readonly string _root;
    private readonly string _mods;

    public CatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Catalog_" + Guid.NewGuid());
        _mods = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(_mods);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // --- Test data ---------------------------------------------------------------------------------

    /// <summary>A CASP resource in the v46 layout measured on real CC.</summary>
    internal static byte[] CasPart(string name, int bodyType, uint ageGender, uint version = 46)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(version);
        w.Write(0u);       // TGI offset
        w.Write(0u);       // presets
        byte[] nameBytes = Encoding.BigEndianUnicode.GetBytes(name);
        w.Write7BitEncodedInt(nameBytes.Length);
        w.Write(nameBytes);
        w.Write(1f);       // sort priority
        w.Write((ushort)0);
        w.Write(0u);       // property id
        w.Write(0u);       // aural material
        int flagBlock = version switch { 40 => 18, <= 49 => 26, 50 => 28, _ => 40 };
        w.Write(new byte[flagBlock]);
        w.Write(2u);       // two tags
        w.Write(new byte[] { 0x44, 0, 0x54, 0, 0, 0, 0x44, 0, 0x48, 0, 0, 0 });
        w.Write(0u);       // price
        w.Write(0u);       // title key
        w.Write(0u);       // description key
        if (version >= 43)
            w.Write(0u);
        w.Write((byte)0);  // unique texture space
        w.Write(bodyType);
        w.Write(0);        // sub type
        w.Write(ageGender);
        w.Write(new byte[32]);
        return stream.ToArray();
    }

    internal static byte[] StringTable(params (uint Key, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(Encoding.ASCII.GetBytes("STBL"));
        w.Write((ushort)5);
        w.Write((byte)0);
        w.Write((ulong)entries.Length);
        w.Write((ushort)0);
        w.Write((uint)entries.Sum(e => Encoding.UTF8.GetByteCount(e.Text) + 1));
        foreach (var (key, text) in entries)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            w.Write(key);
            w.Write((byte)0);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }
        return stream.ToArray();
    }

    private string WritePackage(string relativePath, params TestDbpfBuilder.Entry[] entries)
    {
        string path = Path.Combine(_mods, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));
        return path;
    }

    private string WriteHair(string relativePath, string casName = "Creator_yfHair_Test") =>
        WritePackage(relativePath,
            new TestDbpfBuilder.Entry(CasPartReader.ResourceType, 0, 0x1111_2222_3333_4444, CasPart(casName, 2, 0x2078)),
            new TestDbpfBuilder.Entry(0x3C1AF1F2, 0, 0x1111_2222_3333_4444, new byte[] { 0xFF, 0xD8, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66 }));

    // --- Readers ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(40u)]
    [InlineData(43u)]
    [InlineData(46u)]
    [InlineData(50u)]
    [InlineData(52u)]
    public void ReadsBodyTypeAndAgeGenderOfEveryKnownVersion(uint version)
    {
        var info = CasPartReader.TryRead(CasPart("Maya_yfShoes_Flats", 8, 0x2078, version));

        Assert.NotNull(info);
        Assert.Equal("Maya_yfShoes_Flats", info!.Name);
        Assert.Equal(8, info.BodyType);
        Assert.Equal(0x2078u, info.AgeGender);
    }

    [Fact]
    public void RejectsMisalignedOrUnknownCasParts()
    {
        Assert.Null(CasPartReader.TryRead(new byte[] { 1, 2, 3 }));
        Assert.Null(CasPartReader.TryRead(CasPart("x", 2, 0x2078, version: 32))); // too old to parse
    }

    [Fact]
    public void ReadsStringTables()
    {
        var table = StringTableReader.TryRead(StringTable((0xABCD, "Sessel „Cozy“"), (1, "x")));

        Assert.Equal("Sessel „Cozy“", table[0xABCD]);
        Assert.Equal(2, table.Count);
    }

    [Theory]
    [InlineData(2, CasCategory.Hair)]
    [InlineData(6, CasCategory.Clothing)]
    [InlineData(8, CasCategory.Shoes)]
    [InlineData(12, CasCategory.Accessories)]
    [InlineData(29, CasCategory.Makeup)]
    [InlineData(34, CasCategory.EyesBrows)]
    [InlineData(48, CasCategory.Tattoos)]
    [InlineData(73, CasCategory.Nails)]
    [InlineData(40, CasCategory.Skin)]
    public void MapsBodyTypesToAreas(int bodyType, CasCategory expected) =>
        Assert.Equal(expected, ContentCategories.FromBodyType(bodyType));

    [Fact]
    public void CleansMarkupAndKeywordsFromNames()
    {
        Assert.Equal("VALIA - Charm'd Rose Bush 5", CatalogScanner.CleanName("<font color= '#40357C'><b>VALIA - Charm'd </b></font> Rose Bush 5| Valia Charmd"));
        Assert.Null(CatalogScanner.CleanName("<b></b>"));
    }

    // --- Classification -----------------------------------------------------------------------------

    [Fact]
    public void ClassifiesPackagesByContent()
    {
        WriteHair("hair.package");
        WritePackage("chair.package", new TestDbpfBuilder.Entry(0xC0DB5AE7, 0, 0x5555_0000_0000_0001, new byte[16]));
        WritePackage("floor.package", new TestDbpfBuilder.Entry(0xB4F762C9, 0, 0x5555_0000_0000_0002, new byte[16]));
        WritePackage("gameplay.package",
            new TestDbpfBuilder.Entry(0xE882D22F, 0, 0x5555_0000_0000_0003, new byte[16]),
            new TestDbpfBuilder.Entry(0x545AC67A, 0, 0x5555_0000_0000_0003, new byte[16]));
        WritePackage("default.package", new TestDbpfBuilder.Entry(0x00B2D882, 0, 0x5555_0000_0000_0004, new byte[16]));
        WritePackage("slider.package", new TestDbpfBuilder.Entry(0xC5F6763E, 0, 0x5555_0000_0000_0005, new byte[16]));
        File.WriteAllBytes(Path.Combine(_mods, "script.ts4script"), new byte[] { 0x50, 0x4B, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        var byName = ModScanner.Scan(_mods).ToDictionary(m => m.DisplayName, m => ModClassifier.Classify(m.Files[0]));

        Assert.Equal(ContentCategory.Cas, byName["hair"]);
        Assert.Equal(ContentCategory.Objects, byName["chair"]);
        Assert.Equal(ContentCategory.Build, byName["floor"]);
        Assert.Equal(ContentCategory.Gameplay, byName["gameplay"]);
        Assert.Equal(ContentCategory.AssetsOnly, byName["default"]);
        Assert.Equal(ContentCategory.Sliders, byName["slider"]);
        Assert.Equal(ContentCategory.Script, byName["script"]);
    }

    [Fact]
    public void CatalogScannerReadsCasDetailsAndCachesThem()
    {
        WriteHair("hair.package");
        var mods = ModScanner.Scan(_mods);
        var scanner = new CatalogScanner(Path.Combine(_root, "cache"));

        var info = scanner.Scan(mods).Single().Value;

        Assert.Equal(ContentCategory.Cas, info.Category);
        Assert.Equal(CasCategory.Hair, info.CasCategory);
        Assert.Equal(0x2078u, info.AgeGender);
        Assert.Equal(1, info.ItemCount);
        Assert.NotNull(info.Thumbnail);
        Assert.True(File.Exists(Path.Combine(scanner.ThumbnailDirectory, info.Thumbnail!)));

        // A second scanner reads the cache instead of the package.
        var cached = new CatalogScanner(Path.Combine(_root, "cache")).Scan(mods).Single().Value;
        Assert.Equal(info.Thumbnail, cached.Thumbnail);
        Assert.Equal(CasCategory.Hair, cached.CasCategory);
    }

    [Fact]
    public void CatalogUsesInGameObjectNames()
    {
        var cobj = new byte[32];
        BitConverter.GetBytes(0x1234u).CopyTo(cobj, 8);
        WritePackage("sofa.package",
            new TestDbpfBuilder.Entry(CatalogObjectReader.ResourceType, 0, 0x7777_0000_0000_0001, cobj),
            new TestDbpfBuilder.Entry(0xC0DB5AE7, 0, 0x7777_0000_0000_0001, new byte[16]),
            new TestDbpfBuilder.Entry(StringTableReader.ResourceType, 0, 0x0800_0000_0000_0001, StringTable((0x1234, "Gemütliches Sofa"))));

        var info = new CatalogScanner(Path.Combine(_root, "cache")).Scan(ModScanner.Scan(_mods)).Single().Value;

        Assert.Equal(ContentCategory.Objects, info.Category);
        Assert.Equal("Gemütliches Sofa", info.Name);
    }

    [Fact]
    public void GuessesCreatorsFromNamingConventions()
    {
        var creators = CreatorGuesser.Guess(new[]
        {
            new CreatorGuesser.Input("a", "[Kijiko] Remove Lashes", Array.Empty<string>()),
            new CreatorGuesser.Input("b", "Doll Lashes by Venerian (3D)", Array.Empty<string>()),
            new CreatorGuesser.Input("c", "Baggy Jean Opi~Whimp1337", Array.Empty<string>()),
            new CreatorGuesser.Input("d", "Milk Pants", new[] { "BABYETEARS_yfBottom_Pants_2024" }),
            new CreatorGuesser.Input("e1", "AggressiveKitty Door 1", Array.Empty<string>()),
            new CreatorGuesser.Input("e2", "AggressiveKitty Door 2", Array.Empty<string>()),
            new CreatorGuesser.Input("e3", "AggressiveKitty_Window", Array.Empty<string>()),
            new CreatorGuesser.Input("f", "Lipstick A211", Array.Empty<string>()),
            new CreatorGuesser.Input("g", "Oh So Antique Vanity", Array.Empty<string>()),
            new CreatorGuesser.Input("h", "(((Tyr))) Classic Tiles", Array.Empty<string>()),
        });

        Assert.Equal("Kijiko", creators["a"]);
        Assert.Equal("Venerian", creators["b"]);
        Assert.Equal("Whimp1337", creators["c"]);
        Assert.Equal("BABYETEARS", creators["d"]);
        Assert.Equal("AggressiveKitty", creators["e3"]);
        Assert.False(creators.ContainsKey("f"));
        Assert.False(creators.ContainsKey("g"));
        Assert.Equal("Tyr", creators["h"]);
    }

    // --- Collections and sorting --------------------------------------------------------------------

    [Fact]
    public void CollectionFoldersSplitIntoSeparateModsWithStableIds()
    {
        string collection = Path.Combine(_mods, "Haare");
        WriteHair(Path.Combine("Haare", "a.package"));
        WriteHair(Path.Combine("Haare", "Set", "b.package"));
        Assert.Single(ModScanner.Scan(_mods)); // plain folder = one mod

        File.WriteAllText(Path.Combine(collection, ModScanner.CollectionMarker), "");
        var mods = ModScanner.Scan(_mods);

        Assert.Equal(new[] { "a", "Set" }, mods.Select(m => m.DisplayName));
        Assert.All(mods, m => Assert.Equal("Haare", m.Collection));
        Assert.Equal("a.package", mods[0].Id); // same ID as directly in Mods - profiles keep working
    }

    [Fact]
    public void SortingMovesModsIntoCollectionsAndUndoRestoresEverything()
    {
        WriteHair("Creator hair.package");
        WriteHair(Path.Combine("Hair Set", "h1.package"));
        WriteHair(Path.Combine("Hair Set", "sub", "h2.package"));
        WritePackage("mc_cmd_center.package", new TestDbpfBuilder.Entry(0xE882D22F, 0, 0x9999_0000_0000_0001, new byte[16]));
        File.WriteAllBytes(Path.Combine(_mods, "mc_cmd_center.ts4script"), new byte[] { 1 });
        var before = Directory.GetFiles(_mods, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(_mods, f)).OrderBy(f => f).ToList();

        var mods = ModScanner.Scan(_mods);
        var inputs = mods.Select(m => new SortInput(m,
            m.ContainsScript ? ContentCategory.Script : m.DisplayName.StartsWith("mc") ? ContentCategory.Gameplay : ContentCategory.Cas,
            CasCategory.Hair, m.DisplayName.StartsWith("Creator") ? "Creator" : null)).ToList();
        var plan = ModSorter.Plan(_mods, inputs, new SortOptions { ByCreator = true });

        Assert.Equal(4, plan.Moves.Count);
        Assert.Contains(plan.Moves, m => m.Mod.DisplayName == "Creator hair" && m.TargetFolder == Path.Combine("CAS - Haare", "Creator"));
        Assert.Contains(plan.Moves, m => m.Mod.DisplayName == "mc_cmd_center" && m.TargetFolder == ModSorter.ScriptFolder && m.Mod.ContainsPackage);

        var journal = new ChangeJournal(Path.Combine(_root, "journal"));
        string id;
        using (var recorder = journal.Begin("sort"))
        {
            var result = ModSorter.Apply(plan, recorder);
            Assert.Empty(result.Errors);
            id = recorder.Id;
        }

        var sorted = ModScanner.Scan(_mods);
        Assert.Equal(mods.Select(m => m.Id).OrderBy(i => i), sorted.Select(m => m.Id).OrderBy(i => i)); // same mods, same IDs
        Assert.True(File.Exists(Path.Combine(_mods, "CAS - Haare", "Creator", "Creator hair.package")));
        Assert.True(File.Exists(Path.Combine(_mods, "CAS - Haare", "Hair Set", "sub", "h2.package")));
        Assert.True(File.Exists(Path.Combine(_mods, ModSorter.ScriptFolder, "mc_cmd_center.ts4script"))); // one folder deep
        Assert.False(Directory.Exists(Path.Combine(_mods, "Hair Set")));

        Assert.True(journal.Undo(id).Success);
        var after = Directory.GetFiles(_mods, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(_mods, f)).OrderBy(f => f).ToList();
        Assert.Equal(before, after);
        Assert.False(Directory.Exists(Path.Combine(_mods, "CAS - Haare"))); // created folders removed again
    }

    [Fact]
    public void SortingKeepsScriptFoldersAndRespectsDepthLimit()
    {
        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(_mods, "WickedWhims")).FullName, "ww.ts4script"), new byte[] { 1 });
        WriteHair(Path.Combine("Deep", "1", "2", "3", "4", "deep.package"));

        var mods = ModScanner.Scan(_mods);
        var plan = ModSorter.Plan(_mods, mods.Select(m => new SortInput(m, ContentCategory.Cas, CasCategory.Hair, "X")).ToList(), new SortOptions());

        Assert.Empty(plan.Moves);
        Assert.Equal(2, plan.Skipped.Count);
    }

    // --- Snapshots, notes, backups ------------------------------------------------------------------

    [Fact]
    public void SnapshotComparisonFindsEveryKindOfChange()
    {
        WriteHair("keep.package");
        WriteHair("remove.package");
        WriteHair("toggle.package");
        WriteHair("change.package");
        WriteHair("move.package");
        var before = ModSnapshot.Capture(_mods, ModScanner.Scan(_mods), ModSnapshotStore.ReasonPlay);

        File.Delete(Path.Combine(_mods, "remove.package"));
        File.Move(Path.Combine(_mods, "toggle.package"), Path.Combine(_mods, "toggle.package.disabled"));
        File.AppendAllText(Path.Combine(_mods, "change.package"), "x");
        Directory.CreateDirectory(Path.Combine(_mods, "Sub"));
        File.Move(Path.Combine(_mods, "move.package"), Path.Combine(_mods, "Sub", "move.package"));
        WriteHair("new.package");
        var after = ModSnapshot.Capture(_mods, ModScanner.Scan(_mods), "");

        var diff = ModSnapshotStore.Compare(before, after);

        Assert.Equal("new.package", Assert.Single(diff.Added).Path);
        Assert.Equal("remove.package", Assert.Single(diff.Removed).Path);
        Assert.Equal("change.package", Assert.Single(diff.Changed).Path);
        Assert.Equal("toggle.package", Assert.Single(diff.Disabled).Path);
        Assert.Equal(Path.Combine("Sub", "move.package"), Assert.Single(diff.Moved).To);
        Assert.Empty(diff.Enabled);
    }

    [Fact]
    public void SnapshotStoreTakesOneDailySnapshotAndPrunes()
    {
        WriteHair("a.package");
        var store = new ModSnapshotStore(Path.Combine(_root, "snapshots"));
        var mods = ModScanner.Scan(_mods);

        Assert.True(store.EnsureDaily(_mods, mods));
        Assert.False(store.EnsureDaily(_mods, mods));
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(5);
            store.Save(ModSnapshot.Capture(_mods, mods, ModSnapshotStore.ReasonPlay), keep: 3);
        }
        Assert.Equal(3, store.List(_mods).Count);
    }

    [Fact]
    public void NotesAreStoredPerModAndEmptyNotesRemoved()
    {
        var store = new ModNotesStore(Path.Combine(_root, "notes.json"));
        store.Set("mod.package", new ModNote { Note = "Lieblingshaar", Tags = new() { " Haare ", "haare", "Favorit" } });

        var reloaded = new ModNotesStore(Path.Combine(_root, "notes.json"));
        var note = reloaded.Get("MOD.PACKAGE");
        Assert.Equal("Lieblingshaar", note!.Note);
        Assert.Equal(new[] { "Haare", "Favorit" }, note.Tags);
        Assert.Equal(new[] { "Favorit", "Haare" }, reloaded.AllTags);

        reloaded.Set("mod.package", new ModNote());
        Assert.Null(new ModNotesStore(Path.Combine(_root, "notes.json")).Get("mod.package"));
    }

    [Fact]
    public void ToggleFavoritePersistsAndSurvivesEmptyNoteText()
    {
        var store = new ModNotesStore(Path.Combine(_root, "notes.json"));

        Assert.True(store.ToggleFavorite("mod.package"));
        Assert.True(store.Get("mod.package")!.IsFavorite);

        // Favoriting alone must not be discarded as an "empty" note, even with no note text/tags/links set.
        var reloaded = new ModNotesStore(Path.Combine(_root, "notes.json"));
        Assert.True(reloaded.Get("mod.package")!.IsFavorite);

        Assert.False(reloaded.ToggleFavorite("mod.package"));
        Assert.Null(new ModNotesStore(Path.Combine(_root, "notes.json")).Get("mod.package"));
    }

    [Fact]
    public void TogglingFavoriteDoesNotDiscardExistingNoteText()
    {
        var store = new ModNotesStore(Path.Combine(_root, "notes.json"));
        store.Set("mod.package", new ModNote { Note = "Lieblingshaar" });

        store.ToggleFavorite("mod.package");

        var note = new ModNotesStore(Path.Combine(_root, "notes.json")).Get("mod.package");
        Assert.True(note!.IsFavorite);
        Assert.Equal("Lieblingshaar", note.Note);
    }

    [Fact]
    public void AutomaticSaveBackupsArePrunedPerSlotButManualOnesKept()
    {
        string saves = Path.Combine(_root, "saves");
        Directory.CreateDirectory(saves);
        string slot = Path.Combine(saves, "Slot_00000002.save");
        File.WriteAllBytes(slot, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(slot + ".ver0", new byte[] { 1 });
        var save = new Saves.SaveGameInfo { Path = slot, Slot = 2, VersionFiles = new[] { slot + ".ver0" } };
        var backups = new Saves.SaveBackups(Path.Combine(_root, "backups"));

        backups.Create(save);                   // manual: with versions
        for (int i = 0; i < 4; i++)
        {
            Thread.Sleep(1100);                 // backup folders are named by the second
            Assert.Equal(1, backups.Create(save, automatic: true).FileCount);
        }

        Assert.Equal(2, backups.Prune(keepPerSlot: 2));
        var left = backups.List();
        Assert.Equal(3, left.Count);
        Assert.Single(left, b => !b.IsAutomatic);
    }

    [Fact]
    public void TrayMirrorCopiesOnlyChangesAndArchivesOldVersions()
    {
        string tray = Path.Combine(_root, "Tray");
        Directory.CreateDirectory(tray);
        File.WriteAllText(Path.Combine(tray, "a.trayitem"), "1");
        File.WriteAllText(Path.Combine(tray, "b.trayitem"), "1");
        var mirror = new TrayMirror(Path.Combine(_root, "mirror"));

        Assert.Equal(2, mirror.Update(tray).Copied);
        Assert.Equal(0, mirror.Update(tray).Copied);

        File.WriteAllText(Path.Combine(tray, "a.trayitem"), "22");
        File.Delete(Path.Combine(tray, "b.trayitem"));
        var result = mirror.Update(tray);

        Assert.Equal(1, result.Copied);
        Assert.Equal(2, result.Archived);
        Assert.Equal("22", File.ReadAllText(Path.Combine(mirror.CurrentFolder, "a.trayitem")));
        Assert.Equal(2, Directory.GetFiles(mirror.OlderFolder, "*", SearchOption.AllDirectories).Length);
    }

}
