using System.Text;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Export;
using Sims4ModManager.Core.Game;
using Sims4ModManager.Core.Health;

namespace Sims4ModManager.Core.Tests;

public class HealthTests : IDisposable
{
    private readonly string _root;
    private readonly string _data;
    private readonly string _mods;
    private readonly ChangeJournal _journal;

    public HealthTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Health_" + Guid.NewGuid());
        _data = Path.Combine(_root, "Die Sims 4");
        _mods = Path.Combine(_data, "Mods");
        Directory.CreateDirectory(_mods);
        File.WriteAllText(Path.Combine(_data, "GameVersion.txt"), "");
        Directory.CreateDirectory(Path.Combine(_data, "saves"));
        File.WriteAllLines(Path.Combine(_mods, "Resource.cfg"), ModFolderHealth.StandardResourceCfgLines);
        _journal = new ChangeJournal(Path.Combine(_root, "journal"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Mod(string relative, byte[]? content = null)
    {
        string path = Path.Combine(_mods, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? TestDbpfBuilder.Build(new[] { (0x034AEECBu, 0u, 1UL) }));
        return path;
    }

    private static byte[] DbpfHeader(uint major, uint minor)
    {
        var bytes = new byte[96];
        Encoding.ASCII.GetBytes("DBPF").CopyTo(bytes, 0);
        BitConverter.GetBytes(major).CopyTo(bytes, 4);
        BitConverter.GetBytes(minor).CopyTo(bytes, 8);
        return bytes;
    }

    private HealthIssue Issue(string id) => Assert.Single(ModFolderHealth.Inspect(_mods), i => i.Id == id);

    private void FixAndCheckUndo(HealthIssue issue, Action afterFix, Action afterUndo)
    {
        string id;
        using (var recorder = _journal.Begin(issue.Title))
        {
            id = recorder.Id;
            var result = issue.Fix!(recorder);
            Assert.Empty(result.Errors);
        }
        afterFix();
        Assert.True(_journal.Undo(id).Success);
        afterUndo();
    }

    // --- Options.ini -------------------------------------------------------------------------

    [Fact]
    public void ReadsAndChangesOptionsWithoutTouchingAnythingElse()
    {
        string ini = "[options]\r\nedgescrolling = 1\r\n\r\nmodsdisabled = 1\n\nscriptmodsenabled = 0\n\nshowmodliststartup = 1\n";
        string path = Path.Combine(_data, "Options.ini");
        File.WriteAllText(path, ini);

        var options = GameOptions.TryLoad(_data)!;
        Assert.False(options.ModsEnabled);
        Assert.False(options.ScriptModsEnabled);
        Assert.True(options.ShowModListAtStartup);

        using (var recorder = _journal.Begin("Optionen"))
            GameOptions.Update(_data, new Dictionary<string, bool>
            {
                [GameOptions.ModsDisabledKey] = false,
                [GameOptions.ScriptModsEnabledKey] = true,
                ["newkey"] = true
            }, recorder);

        string updated = File.ReadAllText(path);
        Assert.Equal("[options]\r\nedgescrolling = 1\r\n\r\nmodsdisabled = 0\n\nscriptmodsenabled = 1\n\nshowmodliststartup = 1\r\nnewkey = 1\r\n", updated);
        Assert.True(GameOptions.TryLoad(_data)!.ModsEnabled);
    }

    [Fact]
    public void HealthCheckOffersToReEnableModsAfterPatch()
    {
        File.WriteAllText(Path.Combine(_data, "Options.ini"), "[options]\nmodsdisabled = 1\nscriptmodsenabled = 1\n");

        var report = HealthCheck.Run(_mods, ModScanner.Scan(_mods), null);
        var issue = Assert.Single(report.Issues, i => i.Id == "mods-disabled-in-game");
        Assert.True(issue.IsSafeToFixInBulk);

        FixAndCheckUndo(issue,
            () => Assert.True(GameOptions.TryLoad(_data)!.ModsEnabled),
            () => Assert.False(GameOptions.TryLoad(_data)!.ModsEnabled));
    }

    // --- Game version & patch ----------------------------------------------------------------

    [Fact]
    public void ReadsGameVersionAndDetectsUpdates()
    {
        var bytes = Encoding.ASCII.GetBytes("1.126.73.1030");
        File.WriteAllBytes(Path.Combine(_data, "GameVersion.txt"), BitConverter.GetBytes(bytes.Length).Concat(bytes).ToArray());

        Assert.Equal("1.126.73.1030", GameInfo.TryReadGameVersion(_data));
        Assert.Equal("1.126", GameInfo.ShortVersion("1.126.73.1030"));
        Assert.True(GameInfo.CompareVersions("1.126.73.1030", "1.99.1.1") > 0);

        Assert.True(PatchTracker.Check(_data, "1.121.361.1020")!.IsNewSinceLastCheck);
        Assert.False(PatchTracker.Check(_data, "1.126.73.1030")!.IsNewSinceLastCheck);
        Assert.False(PatchTracker.Check(_data, null)!.IsNewSinceLastCheck); // first run: nothing to compare with
    }

    [Fact]
    public void AtRiskAfterPatchAreScriptsAndGameplayTuningNotFurniture()
    {
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Mod("Hair.package"), old);
        File.SetLastWriteTimeUtc(Mod("Sofa.package", TestDbpfBuilder.Build(new[] { (0xB61DE6B4u, 0u, 1UL), (0x545AC67Au, 0u, 1UL) })), old);
        File.SetLastWriteTimeUtc(Mod("Gameplay.package", TestDbpfBuilder.Build(new[] { (0xE882D22Fu, 0u, 1UL) })), old);
        File.WriteAllBytes(Path.Combine(_mods, "Script.ts4script"), new byte[] { 1 });
        File.SetLastWriteTimeUtc(Path.Combine(_mods, "Script.ts4script"), old);
        File.SetLastWriteTimeUtc(Mod("NewGameplay.package", TestDbpfBuilder.Build(new[] { (0xE882D22Fu, 0u, 2UL) })), DateTime.UtcNow);

        var atRisk = PatchTracker.FindAtRisk(ModScanner.Scan(_mods), new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new[] { "Gameplay.package", "Script.ts4script" },
            atRisk.Select(a => Path.GetFileName(a.File.AbsolutePath)).OrderBy(n => n));
    }

    // --- Cache ---------------------------------------------------------------------------------

    [Fact]
    public void CacheIsStaleAfterModChangesAndClearingCanBeUndone()
    {
        string thumbs = Path.Combine(_data, "localthumbcache.package");
        File.WriteAllText(thumbs, "cache");
        Directory.CreateDirectory(Path.Combine(_data, "cachestr"));
        File.WriteAllText(Path.Combine(_data, "cachestr", "a.cache"), "x");
        File.SetLastWriteTimeUtc(thumbs, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(_data, "cachestr", "a.cache"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Mod("New.package");

        var status = CacheCleaner.GetStatus(_data, ModScanner.Scan(_mods));
        Assert.True(status.IsStale);
        Assert.Equal(2, status.FileCount);

        string id;
        using (var recorder = _journal.Begin("Cache"))
        {
            id = recorder.Id;
            Assert.Equal(2, CacheCleaner.Clear(_data, recorder));
        }
        Assert.False(File.Exists(thumbs));
        _journal.Undo(id);
        Assert.True(File.Exists(thumbs));
    }

    // --- Mods folder rules -------------------------------------------------------------------------

    [Fact]
    public void DetectsSims2And3PackagesBrokenAndEmptyFilesAndSortsThemOut()
    {
        string ts3 = Mod("OldHair.package", DbpfHeader(2, 0));
        string ts2 = Mod("Ancient.package", DbpfHeader(1, 1));
        string broken = Mod("NotReally.package", Encoding.ASCII.GetBytes("<html>404</html>"));
        string empty = Mod("Empty.package", Array.Empty<byte>());
        Mod("Fine.package");

        var wrongGame = Issue("wrong-game");
        Assert.Equal(new[] { ts2, ts3 }.OrderBy(p => p), wrongGame.Paths.OrderBy(p => p));
        Assert.Equal(broken, Assert.Single(Issue("broken-package").Paths));
        Assert.Equal(empty, Assert.Single(Issue("empty-file").Paths));

        FixAndCheckUndo(wrongGame,
            () =>
            {
                Assert.False(File.Exists(ts3));
                Assert.True(File.Exists(Path.Combine(_data, ModFolderHealth.SortedOutFolderName, "OldHair.package")));
            },
            () => Assert.True(File.Exists(ts3)));
    }

    [Fact]
    public void MovesTooDeepScriptsAndPackagesUp()
    {
        string script = Mod(Path.Combine("Creator", "Sub", "Deeper", "Mod.ts4script"), new byte[] { 1 });
        string package = Mod(Path.Combine("a", "b", "c", "d", "e", "f", "Deep.package"));
        Mod(Path.Combine("a", "b", "c", "d", "e", "Ok.package"));

        var scriptIssue = Issue("script-too-deep");
        Assert.Equal(script, Assert.Single(scriptIssue.Paths));
        Assert.Equal(package, Assert.Single(Issue("package-too-deep").Paths));

        FixAndCheckUndo(scriptIssue,
            () => Assert.True(File.Exists(Path.Combine(_mods, "Creator", "Mod.ts4script"))),
            () => Assert.True(File.Exists(script)));

        using (var recorder = _journal.Begin("Tief"))
            Issue("package-too-deep").Fix!(recorder);
        Assert.True(File.Exists(Path.Combine(_mods, "a", "b", "c", "d", "e", "Deep.package")));
    }

    [Fact]
    public void SortsOutClutterAndIncompleteDownloadsButKeepsModData()
    {
        string readme = Mod("Set\\Readme.txt", "hi"u8.ToArray());
        string preview = Mod("preview.png", new byte[] { 1 });
        string partial = Mod("Hair.package.crdownload", new byte[] { 1 });
        string config = Mod("WickedWhims\\settings.ini", "x"u8.ToArray());
        string catalog = Mod("Data\\Strings.tmcatalog", "x"u8.ToArray());

        var clutter = Issue("clutter");
        Assert.Equal(new[] { preview, readme }.OrderBy(p => p), clutter.Paths.OrderBy(p => p));
        Assert.Equal(partial, Assert.Single(Issue("incomplete-download").Paths));

        FixAndCheckUndo(clutter,
            () =>
            {
                Assert.True(File.Exists(Path.Combine(_data, ModFolderHealth.SortedOutFolderName, "Set", "Readme.txt")));
                Assert.True(File.Exists(config));
                Assert.True(File.Exists(catalog));
            },
            () => Assert.True(File.Exists(readme)));
    }

    [Fact]
    public void AdoptsForeignDisableConventionsAndRemovesEmptyFolders()
    {
        Mod("Jacket.packageOFF");
        Directory.CreateDirectory(Path.Combine(_mods, "Gone", "AlsoGone"));

        using (var recorder = _journal.Begin("Übernehmen"))
            Issue("foreign-disabled").Fix!(recorder);
        var mod = Assert.Single(ModScanner.Scan(_mods), m => m.DisplayName == "Jacket");
        Assert.False(mod.IsEnabled);

        var emptyFolders = Issue("empty-folders");
        Assert.Equal(2, emptyFolders.Paths.Count);
        FixAndCheckUndo(emptyFolders,
            () => Assert.False(Directory.Exists(Path.Combine(_mods, "Gone"))),
            () => Assert.True(Directory.Exists(Path.Combine(_mods, "Gone", "AlsoGone"))));
    }

    [Fact]
    public void CreatesOrCompletesResourceCfgKeepingCustomLines()
    {
        string cfg = Path.Combine(_mods, "Resource.cfg");
        File.WriteAllLines(cfg, new[] { "Priority 500", "PackedFile *.package", "PackedFile Data/*Strings.tmcatalog" });

        using (var recorder = _journal.Begin("cfg"))
            Issue("resource-cfg-incomplete").Fix!(recorder);
        var lines = File.ReadAllLines(cfg);
        Assert.Contains("PackedFile */*/*/*/*/*.package", lines);
        Assert.Contains("PackedFile Data/*Strings.tmcatalog", lines);

        File.Delete(cfg);
        FixAndCheckUndo(Issue("resource-cfg-missing"),
            () => Assert.Equal(ModFolderHealth.StandardResourceCfgLines, File.ReadAllLines(cfg)),
            () => Assert.False(File.Exists(cfg)));
    }

    [Fact]
    public void CleanFolderHasNoFolderIssues()
    {
        Mod("Hair.package");
        Mod(Path.Combine("Creator", "Mod.ts4script"), new byte[] { 1 });

        Assert.Empty(ModFolderHealth.Inspect(_mods));
    }

    // --- OneDrive & export ---------------------------------------------------------------------------

    [Fact]
    public void RecognizesFoldersInsideOneDrive()
    {
        string oneDrive = Path.Combine(_root, "OneDrive");
        Assert.True(OneDriveCheck.IsInOneDrive(Path.Combine(oneDrive, "Dokumente", "Electronic Arts"), new[] { oneDrive }));
        Assert.False(OneDriveCheck.IsInOneDrive(Path.Combine(_root, "OneDriveBackup"), new[] { oneDrive }));
    }

    [Fact]
    public void ExportsModListAsTextAndCsv()
    {
        Mod("Hair;Special.package");
        Mod(Path.Combine("Script Mod", "mod.ts4script"), new byte[] { 1 });
        Mod("Off.package.disabled");
        var mods = ModScanner.Scan(_mods);

        string text = ModListExporter.Render(mods, ModListFormat.Text, "1.126.73.1030");
        Assert.Contains("Spielversion 1.126.73.1030", text);
        Assert.Contains("Skript-Mods (1):", text);
        Assert.Contains("Deaktiviert (1):", text);

        string csv = ModListExporter.Render(mods, ModListFormat.Csv);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Contains(lines, l => l.StartsWith("\"Hair;Special\";aktiv", StringComparison.Ordinal));
    }
}
