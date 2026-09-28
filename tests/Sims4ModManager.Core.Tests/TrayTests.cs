using System.IO.Compression;
using Sims4ModManager.Core.Tray;
using static Sims4ModManager.Core.Tests.TrayTestFiles;

namespace Sims4ModManager.Core.Tests;

public class TrayTests : IDisposable
{
    private const uint CasPartType = 0x034AEECB;
    private const uint ObjectDefinitionType = 0xC0DB5AE7;
    private const uint StringTableType = 0x220557DA;

    private const ulong HouseholdId = 0x006f16ac32db0015;
    private const ulong SimId = 0x026f16ac32db0016; // household id + 1 with index byte, as the game does it
    private const ulong LotId = 0x004f15e4a7600045;

    private readonly string _root;
    private readonly string _tray;
    private readonly string _mods;

    public TrayTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Tray_" + Guid.NewGuid());
        _tray = Path.Combine(_root, "Tray");
        _mods = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(_tray);
        Directory.CreateDirectory(_mods);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void WriteTray(uint group, ulong instance, string extension, byte[] content) =>
        File.WriteAllBytes(Path.Combine(_tray, FileName(group, instance, extension)), content);

    private void WriteHousehold(ulong id = HouseholdId, ulong simId = SimId, bool withBinary = true, params ulong[] references)
    {
        WriteTray(1, id, "trayitem", TrayItem(id, 1, "Familie Müller", sims: new[] { ("Hans", "Müller", simId) }));
        if (withBinary)
            WriteTray(0, id, "householdbinary", DataReferencing(references));
        WriteTray(0xd97a5802, id, "hhi", new byte[] { 1, 2, 3 });
        WriteTray(0x13, simId, "sgi", new byte[] { 4, 5, 6 });
    }

    private void WritePackage(string relativePath, params (uint Type, ulong Instance)[] resources)
    {
        string path = Path.Combine(_mods, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, TestDbpfBuilder.Build(resources.Select(r => (r.Type, 0u, r.Instance)).ToList()));
    }

    // --- File names & metadata ---------------------------------------------------------------

    [Theory]
    [InlineData("0x00000001!0x006f16ac32db0015.trayitem", true)]
    [InlineData("0xD97A5802!0x006F16AC32DB0015.HHI", true)]
    [InlineData("Familie_Mueller.trayitem", false)]
    [InlineData("0x00000001!0x006f16ac32db0015.package", false)]
    public void ParsesTrayFileNames(string name, bool valid)
    {
        Assert.Equal(valid, TrayFileName.TryParse(name, out var parsed));
        if (valid)
            Assert.Equal(HouseholdId, parsed.Instance);
    }

    [Fact]
    public void ReadsHouseholdMetadata()
    {
        var metadata = TrayMetadataReader.TryParse(TrayItem(HouseholdId, 1, "Familie Müller", "Maurice",
            sims: new[] { ("Hans", "Müller", SimId), ("Anna", "Müller", SimId + 1) }));

        Assert.NotNull(metadata);
        Assert.Equal(TrayItemType.Household, metadata!.Type);
        Assert.Equal("Familie Müller", metadata.Name);
        Assert.Equal("Maurice", metadata.CreatorName);
        Assert.Equal(new[] { "Hans Müller", "Anna Müller" }, metadata.Sims.Select(s => s.FullName));
        Assert.Equal(SimId, metadata.Sims[0].Id);
        Assert.Equal(2026, metadata.CreatedUtc!.Value.Year);
    }

    [Fact]
    public void ReadsLotMetadata()
    {
        var metadata = TrayMetadataReader.TryParse(TrayItem(LotId, 2, "Villa", lotSize: (64, 30), tags: "noCC,villa"));

        Assert.Equal(TrayItemType.Lot, metadata!.Type);
        Assert.Equal((64, 30), metadata.LotSize);
        Assert.Equal("noCC,villa", metadata.Tags);
    }

    [Fact]
    public void RejectsGarbage() => Assert.Null(TrayMetadataReader.TryParse(new byte[] { 0, 0, 0, 0, 5, 0, 0, 0, 0xFF, 0xFF, 0xFF }));

    // --- Library scan ----------------------------------------------------------------------------

    [Fact]
    public void GroupsHouseholdFilesIncludingSimPortraits()
    {
        WriteHousehold();

        var item = Assert.Single(TrayLibraryScanner.Scan(_tray).Items);

        Assert.Equal(TrayItemType.Household, item.Type);
        Assert.Equal("Familie Müller", item.DisplayName);
        Assert.Equal(4, item.Files.Count);
        Assert.Contains(item.Files, f => f.EndsWith(".sgi"));
        Assert.True(item.IsComplete);
    }

    [Fact]
    public void AssignsPortraitByIdSchemeWhenMetadataHasNoSims()
    {
        WriteTray(1, HouseholdId, "trayitem", TrayItem(HouseholdId, 1, "Ohne Sim-Liste"));
        WriteTray(0, HouseholdId, "householdbinary", DataReferencing());
        WriteTray(0x13, SimId, "sgi", new byte[] { 1 });

        var item = Assert.Single(TrayLibraryScanner.Scan(_tray).Items);
        Assert.Equal(3, item.Files.Count);
    }

    [Fact]
    public void ReportsIncompleteItems()
    {
        WriteHousehold(withBinary: false);                                  // .householdbinary missing
        WriteTray(0, LotId, "blueprint", new byte[] { 1 });                 // .trayitem missing

        var items = TrayLibraryScanner.Scan(_tray).Items;

        var household = Assert.Single(items, i => i.Id == HouseholdId);
        Assert.Contains(household.Problems, p => p.Contains(".householdbinary"));
        var orphan = Assert.Single(items, i => i.Id == LotId);
        Assert.Equal(TrayItemType.Lot, orphan.Type);
        Assert.Contains(orphan.Problems, p => p.Contains(".trayitem"));
    }

    // --- CC analysis ----------------------------------------------------------------------------

    [Fact]
    public void FindsReferencedCcIncludingDisabledFilesAndIgnoresIrrelevantTypes()
    {
        const ulong hair = 0xAAAA_BBBB_CCCC_0001, sofa = 0xAAAA_BBBB_CCCC_0002, text = 0xAAAA_BBBB_CCCC_0003;
        WritePackage("Hair.package", (CasPartType, hair));
        WritePackage(Path.Combine("Furniture", "Sofa.package.disabled"), (ObjectDefinitionType, sofa));
        WritePackage("Strings.package", (StringTableType, text));
        WritePackage("Unused.package", (CasPartType, 0xAAAA_BBBB_CCCC_0009));
        WriteHousehold(references: new[] { hair, sofa, text });

        var analyzer = new TrayCcAnalyzer(ModScanner.Scan(_mods));
        var refs = analyzer.Analyze(TrayLibraryScanner.Scan(_tray).Items.Single());

        Assert.Equal(2, refs.Count);
        var hairRef = Assert.Single(refs, r => r.Mod.DisplayName == "Hair");
        Assert.True(hairRef.IsEnabled);
        Assert.Equal(new[] { "CAS" }, hairRef.Categories);
        var sofaRef = Assert.Single(refs, r => r.Mod.DisplayName == "Furniture");
        Assert.False(sofaRef.IsEnabled);
        Assert.Equal(new[] { "Objekte" }, sofaRef.Categories);
    }

    [Fact]
    public void FindsReferencesStoredAsRawLittleEndianValues()
    {
        const ulong wall = 0x1234_5678_9ABC_DEF0;
        WritePackage("Wall.package", (0xD5F0F921, wall));
        WriteTray(2, LotId, "trayitem", TrayItem(LotId, 2, "Haus"));
        WriteTray(0, LotId, "blueprint", new byte[] { 9, 9, 9 }.Concat(BitConverter.GetBytes(wall)).Concat(new byte[] { 7 }).ToArray());

        var refs = new TrayCcAnalyzer(ModScanner.Scan(_mods)).Analyze(TrayLibraryScanner.Scan(_tray).Items.Single());

        Assert.Equal("Wall", Assert.Single(refs).Mod.DisplayName);
    }

    // --- Installer ----------------------------------------------------------------------------

    private string CreateDownloadZip(string name, bool nested = false)
    {
        string zipPath = Path.Combine(_root, name);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        void Add(string entry, byte[] content)
        {
            using var s = zip.CreateEntry(entry).Open();
            s.Write(content);
        }

        Add("Tray Files/" + FileName(1, HouseholdId, "trayitem"), TrayItem(HouseholdId, 1, "Download"));
        Add("Tray Files/" + FileName(0, HouseholdId, "householdbinary"), DataReferencing());
        Add("readme.txt", "Viel Spaß"u8.ToArray());

        byte[] package = TestDbpfBuilder.Build(new[] { (CasPartType, 0u, 0xAAAA_0000_0000_0001UL) });
        if (nested)
        {
            using var inner = new MemoryStream();
            using (var innerZip = new ZipArchive(inner, ZipArchiveMode.Create, leaveOpen: true))
            using (var s = innerZip.CreateEntry("CC/Deep/Hair.package").Open())
                s.Write(package);
            Add("CC.zip", inner.ToArray());
        }
        else
        {
            Add("CC/Hair.package", package);
        }
        return zipPath;
    }

    [Fact]
    public void InstallsDownloadArchiveIntoTrayAndMods()
    {
        string zip = CreateDownloadZip("Familie Download.zip", nested: true);

        using (var plan = TrayInstaller.Analyze(new[] { zip }, _tray, _mods))
        {
            Assert.Equal(2, plan.Count(InstallTargetKind.Tray, InstallStatus.New));
            Assert.Equal(1, plan.Count(InstallTargetKind.Mods, InstallStatus.New));
            Assert.Single(plan.Entries, e => e.Kind == InstallTargetKind.Ignored);
            Assert.Empty(plan.Warnings);

            var result = TrayInstaller.Execute(plan);
            Assert.Equal(3, result.Installed);
            Assert.Empty(result.Errors);
        }

        Assert.True(File.Exists(Path.Combine(_tray, FileName(1, HouseholdId, "trayitem"))));        // flat, name unchanged
        Assert.True(File.Exists(Path.Combine(_mods, "Familie Download", "Hair.package")));          // one level deep
        Assert.Single(TrayLibraryScanner.Scan(_tray).Items);

        using var again = TrayInstaller.Analyze(new[] { zip }, _tray, _mods);
        Assert.True(again.NothingToDo);
        Assert.Equal(3, again.Entries.Count(e => e.Status == InstallStatus.AlreadyInstalled));
    }

    [Fact]
    public void DoesNotOverwriteDifferentExistingFiles()
    {
        string zip = CreateDownloadZip("Download.zip");
        WriteTray(1, HouseholdId, "trayitem", TrayItem(HouseholdId, 1, "Schon da, aber anders"));

        using var plan = TrayInstaller.Analyze(new[] { zip }, _tray, _mods);
        var conflict = Assert.Single(plan.Entries, e => e.Status == InstallStatus.Conflict);
        Assert.Equal(InstallTargetKind.Tray, conflict.Kind);

        TrayInstaller.Execute(plan);
        Assert.Equal("Schon da, aber anders", TrayMetadataReader.TryRead(conflict.TargetPath!)!.Name);
    }

    [Fact]
    public void RecognizesModAlreadyInstalledElsewhereInMods()
    {
        string zip = CreateDownloadZip("Download.zip");
        Directory.CreateDirectory(Path.Combine(_mods, "Alt"));
        File.WriteAllBytes(Path.Combine(_mods, "Alt", "Hair.package.disabled"),
            TestDbpfBuilder.Build(new[] { (CasPartType, 0u, 0xAAAA_0000_0000_0001UL) }));

        using var plan = TrayInstaller.Analyze(new[] { zip }, _tray, _mods);

        var mod = Assert.Single(plan.Entries, e => e.Kind == InstallTargetKind.Mods);
        Assert.Equal(InstallStatus.AlreadyInstalled, mod.Status);
        Assert.Contains("deaktiviert", mod.Note);
    }

    [Fact]
    public void WarnsAboutIncompleteTrayDownloads()
    {
        string folder = Path.Combine(_root, "Nur Bilder");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, FileName(2, LotId, "bpi")), new byte[] { 1 });

        using var plan = TrayInstaller.Analyze(new[] { folder }, _tray, _mods);

        Assert.Contains(plan.Warnings, w => w.Contains(".trayitem"));
    }

    // --- Housekeeping, export, deletion, backup -----------------------------------------------------

    [Fact]
    public void FindsAndFixesMisplacedFiles()
    {
        string trayInMods = Path.Combine(_mods, "Falsch", FileName(1, HouseholdId, "trayitem"));
        Directory.CreateDirectory(Path.GetDirectoryName(trayInMods)!);
        File.WriteAllBytes(trayInMods, TrayItem(HouseholdId, 1, "Verirrt"));
        File.WriteAllBytes(Path.Combine(_tray, "Hair.package"), new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(_tray, "Downloads"));
        File.WriteAllBytes(Path.Combine(_tray, "Downloads", FileName(0, LotId, "blueprint")), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_tray, "Download.zip"), new byte[] { 1 });

        var issues = TrayHousekeeping.Inspect(_tray, _mods);

        Assert.Equal(4, issues.Count);
        Assert.Contains(issues, i => i.Kind == PlacementIssueKind.ArchiveNotExtracted && i.SuggestedTarget is null);

        var journal = new Backup.ChangeJournal(Path.Combine(_root, "journal"));
        string setId;
        using (var recorder = journal.Begin("Aufräumen"))
        {
            setId = recorder.Id;
            foreach (var issue in issues.Where(i => i.SuggestedTarget is not null))
                Assert.Null(TrayHousekeeping.TryFix(issue, recorder));
        }

        Assert.True(File.Exists(Path.Combine(_tray, FileName(1, HouseholdId, "trayitem"))));
        Assert.True(File.Exists(Path.Combine(_tray, FileName(0, LotId, "blueprint"))));
        Assert.True(File.Exists(Path.Combine(_mods, "Hair.package")));
        Assert.Single(TrayHousekeeping.Inspect(_tray, _mods)); // only the archive remains

        // Undo puts everything back where it was.
        Assert.True(journal.Undo(setId).Success);
        Assert.Equal(4, TrayHousekeeping.Inspect(_tray, _mods).Count);
    }

    [Fact]
    public void ExportsItemWithCcAsFolderAndZip()
    {
        const ulong hair = 0xAAAA_BBBB_CCCC_0001;
        WritePackage(Path.Combine("Hair Set", "Sub", "Hair.package.disabled"), (CasPartType, hair));
        WriteHousehold(references: hair);
        var item = TrayLibraryScanner.Scan(_tray).Items.Single();
        var cc = new TrayCcAnalyzer(ModScanner.Scan(_mods)).Analyze(item);
        string exportDir = Path.Combine(_root, "export");
        Directory.CreateDirectory(exportDir);

        string folder = TrayMaintenance.Export(item, cc, exportDir, asZip: false);
        string zip = TrayMaintenance.Export(item, cc, exportDir, asZip: true);

        Assert.Equal(4, Directory.GetFiles(Path.Combine(folder, "Tray")).Length);
        Assert.True(File.Exists(Path.Combine(folder, "Mods", "Hair Set", "Sub", "Hair.package"))); // exported enabled
        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "Mods/Hair Set/Sub/Hair.package");
        Assert.Equal(5, archive.Entries.Count);
    }

    [Fact]
    public void DeletingMovesFilesToBackupAndBackupCanBeReinstalled()
    {
        WriteHousehold();
        var item = TrayLibraryScanner.Scan(_tray).Items.Single();

        var journal = new Backup.ChangeJournal(Path.Combine(_root, "journal"));
        string backup, setId;
        using (var recorder = journal.Begin("Entfernen"))
        {
            setId = recorder.Id;
            backup = TrayMaintenance.DeleteToBackup(item, recorder);
        }

        Assert.Empty(TrayLibraryScanner.Scan(_tray).Items);
        Assert.Equal(4, Directory.GetFiles(Path.Combine(backup, "files"), "*", SearchOption.AllDirectories).Length);

        // Restoring works both via the journal ...
        Assert.True(journal.Undo(setId).Success);
        Assert.Single(TrayLibraryScanner.Scan(_tray).Items);

        // ... and by re-installing a backup folder like a download.
        using (var recorder = journal.Begin("Entfernen 2"))
            backup = TrayMaintenance.DeleteToBackup(TrayLibraryScanner.Scan(_tray).Items.Single(), recorder);
        using var plan = TrayInstaller.Analyze(new[] { Path.Combine(backup, "files") }, _tray, _mods);
        TrayInstaller.Execute(plan);
        Assert.Single(TrayLibraryScanner.Scan(_tray).Items);
    }

    [Fact]
    public void CreatesBackupZipOfSelectedFolders()
    {
        WriteHousehold();
        Directory.CreateDirectory(Path.Combine(_root, "saves"));
        File.WriteAllText(Path.Combine(_root, "saves", "Slot_00000001.save"), "save");
        WritePackage("Hair.package", (CasPartType, 1UL << 40));
        string zipPath = Path.Combine(_root, "backup.zip");

        var skipped = TrayMaintenance.CreateBackup(_root, zipPath, includeTray: true, includeSaves: true, includeMods: false);

        Assert.Empty(skipped);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(5, zip.Entries.Count);
        Assert.Contains(zip.Entries, e => e.FullName == "saves/Slot_00000001.save");
        Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("Mods/"));
    }
}
