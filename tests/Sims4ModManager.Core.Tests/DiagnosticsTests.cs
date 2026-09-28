using System.IO.Compression;
using System.Net;
using System.Text;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Diagnostics;
using Sims4ModManager.Core.Saves;
using Sims4ModManager.Core.Tray;
using static Sims4ModManager.Core.Tests.TrayTestFiles;

namespace Sims4ModManager.Core.Tests;

public class DiagnosticsTests : IDisposable
{
    private readonly string _root;
    private readonly string _data;
    private readonly string _mods;
    private readonly ChangeJournal _journal;

    public DiagnosticsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Diag_" + Guid.NewGuid());
        _data = Path.Combine(_root, "Die Sims 4");
        _mods = Path.Combine(_data, "Mods");
        Directory.CreateDirectory(_mods);
        Directory.CreateDirectory(Path.Combine(_data, "saves"));
        var version = Encoding.ASCII.GetBytes("1.126.73.1030");
        File.WriteAllBytes(Path.Combine(_data, "GameVersion.txt"), BitConverter.GetBytes(version.Length).Concat(version).ToArray());
        _journal = new ChangeJournal(Path.Combine(_root, "journal"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>RefPack stream using literal blocks only (valid, just not compressed).</summary>
    private static byte[] RefPackLiteral(byte[] data)
    {
        var output = new List<byte> { 0x10, 0xFB, (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
        int pos = 0;
        while (data.Length - pos >= 4)
        {
            int block = Math.Min(112, (data.Length - pos) / 4 * 4);
            output.Add((byte)(0xE0 | ((block - 4) >> 2)));
            output.AddRange(data.Skip(pos).Take(block));
            pos += block;
        }
        output.Add((byte)(0xFC | (data.Length - pos)));
        output.AddRange(data.Skip(pos));
        return output.ToArray();
    }

    private static string Script(string path, params (string Entry, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }
        return path;
    }

    // --- RefPack ---------------------------------------------------------------------------------

    [Fact]
    public void RefPackDecodesLiteralsAndBackReferences()
    {
        byte[] withBackReference = { 0x10, 0xFB, 0x00, 0x00, 0x09, 0x0F, 0x02, (byte)'a', (byte)'b', (byte)'c', 0xFC };
        Assert.Equal("abcabcabc", Encoding.ASCII.GetString(RefPack.Decompress(withBackReference)));

        byte[] data = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7)).ToArray();
        Assert.Equal(data, RefPack.Decompress(RefPackLiteral(data)));

        Assert.Throws<InvalidDataException>(() => RefPack.Decompress(new byte[] { 0x10, 0xFB, 0, 0, 50, 0xFC }));
    }

    // --- Saves -----------------------------------------------------------------------------------

    private string Save(int slot, string name, string version, string[] households, params ulong[] ccReferences)
    {
        var body = new Proto()
            .Message(3, new Proto().String(2, name).String(10, version).String(17, "1.118.257.1020"));
        foreach (string household in households)
            body.Message(5, new Proto().String(3, household));
        body.Message(6, new Proto().Varint(1, 1)).Message(7, new Proto().Varint(1, 1));

        var entries = new List<TestDbpfBuilder.Entry>
        {
            new(0x0000000D, 0, 1, RefPackLiteral(body.ToArray()), 0xFFFF),
            new(0x00000006, 0, 2, DataReferencing(ccReferences), TestDbpfBuilder.Zlib)
        };
        string path = Path.Combine(_data, "saves", $"Slot_{slot:X8}.save");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));
        return path;
    }

    [Fact]
    public void ReadsSaveOverviewAndUsedCc()
    {
        const ulong hair = 0xAAAA_BBBB_CCCC_0001;
        string path = Save(3, "Meine Welt", "1.126.73.1030", new[] { "Familie Müller", "Goth" }, hair);
        File.WriteAllText(path + ".ver0", "old");
        Directory.CreateDirectory(Path.Combine(_mods, "CC"));
        File.WriteAllBytes(Path.Combine(_mods, "CC", "Hair.package"), TestDbpfBuilder.Build(new[] { (0x034AEECBu, 0u, hair) }));

        var save = Assert.Single(SaveGameReader.List(_data));
        Assert.Equal(3, save.Slot);
        Assert.Equal("Meine Welt", save.Name);
        Assert.Equal("1.126.73.1030", save.SavedWithVersion);
        Assert.Equal(new[] { "Familie Müller", "Goth" }, save.HouseholdNames);
        Assert.Equal(1, save.SimCount);
        Assert.Single(save.VersionFiles);

        var cc = SaveGameReader.FindUsedCc(path, new TrayCcAnalyzer(ModScanner.Scan(_mods)));
        Assert.Equal("Hair.package", Path.GetFileName(Assert.Single(cc).File.AbsolutePath));
    }

    [Fact]
    public void BacksUpAndRestoresSavesThroughTheJournal()
    {
        string path = Save(2, "Alt", "1.126", new[] { "A" });
        var backups = new SaveBackups(Path.Combine(_root, "save-backups"));
        var backup = backups.Create(SaveGameReader.List(_data).Single());
        Assert.Equal("Alt", Assert.Single(backups.List()).Name);

        Save(2, "Neu", "1.126", new[] { "B" });
        string id;
        using (var recorder = _journal.Begin("Wiederherstellen"))
        {
            id = recorder.Id;
            backups.Restore(backup, _data, recorder);
        }
        Assert.Equal("Alt", SaveGameReader.List(_data).Single().Name);

        _journal.Undo(id);
        Assert.Equal("Neu", SaveGameReader.List(_data).Single().Name);
        Assert.True(File.Exists(path));
    }

    // --- Error logs ------------------------------------------------------------------------------

    [Fact]
    public void AssignsScriptErrorsToModsAndFlagsOldOrMissingOnes()
    {
        Script(Path.Combine(_mods, "Horror", "HorrorLife.ts4script"), ("melunn_horrorlife.pyc", "code"));
        string traceback = "Traceback (most recent call last):\r\n" +
                           "  File \"T:\\InGame\\Gameplay\\Scripts\\Core\\sims4\\tuning\\instance_manager.py\", line 251\r\n" +
                           "  File \"C:\\Docs\\Mods\\Horror\\HorrorLife.ts4script\\melunn_horrorlife.py\", line 10, in _inject\r\n" +
                           "TypeError: boom";
        string gone = "  File \"C:\\Docs\\Mods\\Old.ts4script\\old_mod.py\", line 1\r\nKeyError: x";
        string Report(string category, string version, string text) =>
            $"<report><type>desync</type><createtime>2026-07-12 11:10:09</createtime><buildsignature>Local.Unknown.Unknown.{version}-1.300.Release</buildsignature>" +
            $"<categoryid>{category}</categoryid><desyncdata>{WebUtility.HtmlEncode(text).Replace("\r\n", "&#13;&#10;")}</desyncdata></report>";
        File.WriteAllText(Path.Combine(_data, "lastException_1.txt"), "<?xml version=\"1.0\" ?><root>" +
            Report("melunn_HorrorLife.py:10", "1.126.73.1030", traceback) + Report("melunn_HorrorLife.py:10", "1.126.73.1030", traceback) +
            Report("old_mod.py:1", "1.121.361.1020", gone) + "</root>");
        File.WriteAllText(Path.Combine(_data, "lastCrash_1.txt"),
            "<report><type>crash</type><createtime>2026-08-01 12:17:00</createtime><buildsignature>x.1.125.59.1030-y</buildsignature><categoryid> 0x5e3962c2</categoryid><stack>0x1</stack></report>");

        var entries = ErrorLogAnalyzer.Analyze(_data, _mods, ModScanner.Scan(_mods));
        var groups = ErrorLogAnalyzer.Group(entries);

        var horror = Assert.Single(groups, g => g.Category == "melunn_HorrorLife.py:10");
        Assert.Equal(2, horror.Count);
        Assert.Equal("HorrorLife.ts4script", Path.GetFileName(Assert.Single(horror.Latest.Suspects).File.AbsolutePath));
        Assert.False(horror.Latest.IsFromOlderGameVersion);
        Assert.Contains("TypeError: boom", horror.Latest.Details);

        var old = Assert.Single(groups, g => g.Category == "old_mod.py:1");
        Assert.True(old.Latest.IsFromOlderGameVersion);
        Assert.Equal("Old.ts4script", Assert.Single(old.Latest.MissingScripts));

        var crash = Assert.Single(groups, g => g.Kind == ErrorLogKind.Crash);
        Assert.Empty(crash.Latest.Suspects);
    }

    // --- 50/50 -----------------------------------------------------------------------------------

    [Fact]
    public void BisectFindsTheCulpritAndRestoresEverything()
    {
        for (int i = 0; i < 13; i++)
            File.WriteAllBytes(Path.Combine(_mods, $"Mod{i:D2}.package"), new byte[] { 1 });
        string culprit = Path.Combine(_mods, "Mod09.package");
        var session = new BisectSession(Path.Combine(_root, "bisect.json"));

        BisectState state;
        using (var r = _journal.Begin("Start"))
            state = session.Start(_mods, ModScanner.Scan(_mods), "Absturz", r);
        int rounds = 1;
        while (!state.IsFinished)
        {
            bool problemStillThere = File.Exists(culprit); // the culprit still loads
            using var r = _journal.Begin("Runde");
            state = session.Answer(state, problemStillThere, r);
            rounds++;
        }

        Assert.Equal(new[] { "Mod09.package" }, BisectSession.SuspectFiles(state));
        Assert.True(rounds <= 5, $"{rounds} rounds for 13 mods");
        Assert.All(ModScanner.Scan(_mods), m => Assert.True(m.IsEnabled));
        Assert.NotNull(session.Load()); // result stays available until dismissed

        using (var r = _journal.Begin("Stop"))
            session.Stop(state, r);
        Assert.Null(session.Load());
    }

    [Fact]
    public void StoppingBisectMidwayReEnablesTheTestedHalf()
    {
        for (int i = 0; i < 6; i++)
            File.WriteAllBytes(Path.Combine(_mods, $"Mod{i}.package"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_mods, "Off.package.disabled"), new byte[] { 1 });
        var session = new BisectSession(Path.Combine(_root, "bisect.json"));

        BisectState state;
        using (var r = _journal.Begin("Start"))
            state = session.Start(_mods, ModScanner.Scan(_mods), "Test", r);
        Assert.Equal(3, ModScanner.Scan(_mods).Count(m => !m.IsEnabled && m.DisplayName.StartsWith("Mod")));

        using (var r = _journal.Begin("Stop"))
            session.Stop(session.Load()!, r);

        var mods = ModScanner.Scan(_mods);
        Assert.All(mods.Where(m => m.DisplayName.StartsWith("Mod")), m => Assert.True(m.IsEnabled));
        Assert.False(mods.Single(m => m.DisplayName == "Off").IsEnabled); // was off before - stays off
    }

    // --- Safety & dependencies -------------------------------------------------------------------

    [Fact]
    public void FlagsSuspiciousScriptContent()
    {
        string clean = Script(Path.Combine(_mods, "Clean.ts4script"), ("mod/main.pyc", "import sims4.commands"));
        string network = Script(Path.Combine(_mods, "Updater.ts4script"), ("upd/check.pyc", "urlopen"));
        string bad = Script(Path.Combine(_mods, "Bad.ts4script"), ("x/run.pyc", "subprocess b64decode exec compile"), ("x/payload.exe", "MZ"));

        Assert.Empty(ScriptSafetyScanner.ScanArchive(clean));
        Assert.All(ScriptSafetyScanner.ScanArchive(network), f => Assert.Equal(ScriptRisk.Notice, f.Risk));
        var findings = ScriptSafetyScanner.ScanArchive(bad);
        Assert.Equal(2, findings.Count(f => f.Risk == ScriptRisk.Dangerous));
        Assert.Contains(findings, f => f.Risk == ScriptRisk.Suspicious);
    }

    [Fact]
    public void DetectsMissingAndDisabledLibraries()
    {
        Script(Path.Combine(_mods, "Fancy", "Fancy.ts4script"), ("fancy/main.pyc", "from sims4communitylib.utils import x"));
        Assert.Equal(DependencyState.Missing, Assert.Single(DependencyChecker.Check(ModScanner.Scan(_mods))).State);

        Script(Path.Combine(_mods, "S4CL", "sims4communitylib.ts4script.disabled"), ("sims4communitylib/__init__.pyc", "lib"));
        var disabled = Assert.Single(DependencyChecker.Check(ModScanner.Scan(_mods)));
        Assert.Equal(DependencyState.Disabled, disabled.State);
        Assert.Equal("S4CL", disabled.LibraryMod!.DisplayName);

        ModToggleService.SetEnabled(disabled.LibraryMod, true);
        Assert.Empty(DependencyChecker.Check(ModScanner.Scan(_mods)));
    }
}
