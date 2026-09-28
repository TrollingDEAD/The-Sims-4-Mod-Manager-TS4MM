using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Conflicts;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Tests;

public class ResolutionTests : IDisposable
{
    private const uint CasPart = 0x034AEECB;
    private const uint SkinTone = 0x0354796A;
    private const uint S4sManifest = 0x7FB6AD8A;

    private readonly string _root;
    private readonly string _mods;
    private readonly ChangeJournal _journal;

    public ResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Resolve_" + Guid.NewGuid());
        _mods = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(_mods);
        _journal = new ChangeJournal(Path.Combine(_root, "journal"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Writes a package; each resource's content is derived from <paramref name="variant"/> so versions differ.</summary>
    private string Package(string relativePath, int variant, DateTime? lastWrite, params (uint Type, ulong Instance)[] resources)
    {
        string path = Path.Combine(_mods, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var entries = resources
            .Select(r => new TestDbpfBuilder.Entry(r.Type, 0, r.Instance, BitConverter.GetBytes(r.Instance * 31 + (ulong)variant)))
            .ToList();
        File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));
        if (lastWrite is not null)
            File.SetLastWriteTimeUtc(path, lastWrite.Value);
        return path;
    }

    private (IReadOnlyList<ModEntry> Mods, ConflictReport Report, IReadOnlyList<ResolutionProposal> Proposals) Analyze()
    {
        var mods = ModScanner.Scan(_mods);
        var report = ConflictDetector.FindConflicts(mods);
        return (mods, report, ConflictResolver.ProposeAll(report));
    }

    private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // --- Change journal ---------------------------------------------------------------------

    [Fact]
    public void JournalUndoesMovesReplacementsCreationsAndDeletions()
    {
        string moved = Path.Combine(_root, "a.txt");
        string modified = Path.Combine(_root, "b.txt");
        string deleted = Path.Combine(_root, "c.txt");
        string source = Path.Combine(_root, "source.txt");
        string created = Path.Combine(_root, "sub", "d.txt");
        File.WriteAllText(moved, "A");
        File.WriteAllText(modified, "B-original");
        File.WriteAllText(deleted, "C");
        File.WriteAllText(source, "D");

        string id;
        using (var recorder = _journal.Begin("Test"))
        {
            id = recorder.Id;
            recorder.Move(moved, moved + ".disabled");
            recorder.Replace(modified, temp => File.WriteAllText(temp, "B-new"));
            recorder.Delete(deleted);
            recorder.CopyIn(source, created);
        }

        Assert.Equal("B-new", File.ReadAllText(modified));
        Assert.False(File.Exists(deleted));
        var set = Assert.Single(_journal.List());
        Assert.Equal(5, set.Operations.Count); // incl. the new folder "sub"
        Assert.True(set.Completed);

        var result = _journal.Undo(id);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("A", File.ReadAllText(moved));
        Assert.False(File.Exists(moved + ".disabled"));
        Assert.Equal("B-original", File.ReadAllText(modified));
        Assert.Equal("C", File.ReadAllText(deleted));
        Assert.False(File.Exists(created));
        Assert.False(Directory.Exists(Path.GetDirectoryName(created))); // created folder removed again
        Assert.True(_journal.List().Single().IsUndone);
        Assert.False(_journal.Undo(id).Success); // not twice
    }

    [Fact]
    public void JournalUndoNeverOverwritesFilesThatChangedMeanwhile()
    {
        string file = Path.Combine(_root, "x.package");
        File.WriteAllText(file, "x");
        string id;
        using (var recorder = _journal.Begin("Deaktivieren"))
        {
            id = recorder.Id;
            recorder.Move(file, file + ".disabled");
        }
        File.WriteAllText(file, "a new file with the old name");

        var result = _journal.Undo(id);

        Assert.False(result.Success);
        Assert.Equal("a new file with the old name", File.ReadAllText(file));
        Assert.True(File.Exists(file + ".disabled"));
    }

    [Fact]
    public void EmptyChangeSetsLeaveNoTraceAndOldOnesArePruned()
    {
        using (_journal.Begin("Nichts")) { }
        Assert.Empty(_journal.List());

        for (int i = 0; i < 5; i++)
        {
            string f = Path.Combine(_root, $"{i}.txt");
            File.WriteAllText(f, "");
            using var r = _journal.Begin($"Set {i}");
            r.Delete(f);
        }
        Assert.Equal(2, _journal.Prune(keep: 3));
        Assert.Equal(3, _journal.List().Count);
    }

    [Fact]
    public void ToggleWithRecorderCanBeUndone()
    {
        Package("Hair.package", 0, null, (CasPart, 1UL << 40));
        var mod = ModScanner.Scan(_mods).Single();

        string id;
        using (var recorder = _journal.Begin("Mod deaktiviert"))
        {
            id = recorder.Id;
            Assert.True(ModToggleService.SetEnabled(mod, false, recorder).Success);
        }
        Assert.False(ModScanner.Scan(_mods).Single().IsEnabled);

        _journal.Undo(id);
        Assert.True(ModScanner.Scan(_mods).Single().IsEnabled);
    }

    // --- Package editor -------------------------------------------------------------------

    [Fact]
    public void WriterRemovesOnlyTheRequestedResourcesAndKeepsPayloadsIntact()
    {
        var entries = new[]
        {
            new TestDbpfBuilder.Entry(CasPart, 0, 1, new byte[] { 1, 2, 3 }),
            new TestDbpfBuilder.Entry(CasPart, 0, 2, Enumerable.Repeat((byte)7, 3000).ToArray(), TestDbpfBuilder.Zlib),
            new TestDbpfBuilder.Entry(SkinTone, 5, 0x1234_5678_9ABC_DEF0, new byte[] { 9 }),
        };
        string source = Path.Combine(_root, "in.package");
        string target = Path.Combine(_root, "out.package");
        File.WriteAllBytes(source, TestDbpfBuilder.Build(entries));

        int removed = DbpfWriter.WriteWithout(source, new HashSet<ResourceKey> { new(CasPart, 0, 1) }, target);

        Assert.Equal(1, removed);
        var before = DbpfReader.TryReadIndex(source)!;
        var after = DbpfReader.TryReadIndex(target)!;
        Assert.Equal(2, after.Count);
        Assert.DoesNotContain(after, r => r.Key == new ResourceKey(CasPart, 0, 1));

        var hashesBefore = DbpfReader.TryHashResources(source, before, decompress: true);
        var hashesAfter = DbpfReader.TryHashResources(target, after, decompress: true);
        foreach (var r in after)
            Assert.Equal(hashesBefore[before.Single(b => b.Key == r.Key)], hashesAfter[r]);
    }

    // --- Resolver -------------------------------------------------------------------------

    [Fact]
    public void DuplicateFileKeepsNewerCopy()
    {
        Package("Loose.package", 1, Old, (CasPart, 100), (CasPart, 101));
        Package(Path.Combine("Set", "Loose.package"), 2, New, (CasPart, 100), (CasPart, 101));

        var proposal = Assert.Single(Analyze().Proposals);

        Assert.Equal(ResolutionKind.DuplicateFile, proposal.Kind);
        Assert.Equal(Path.Combine(_mods, "Loose.package"), proposal.Change.File.AbsolutePath);
        Assert.True(proposal.IsSafe);
    }

    [Fact]
    public void QualityVariantKeepsHq()
    {
        Package("Hair_NonHQ.package", 1, New, (CasPart, 100));
        Package("Hair.package", 2, Old, (CasPart, 100));

        var proposal = Analyze().Proposals.Single(p => p.IsRecommended);

        Assert.Equal(ResolutionKind.QualityVariant, proposal.Kind);
        Assert.EndsWith("Hair_NonHQ.package", proposal.Change.File.AbsolutePath);
    }

    [Fact]
    public void VersionMarkersBeatFileDates()
    {
        // "_Fixed" is the newer version even though its file is older (copying resets dates).
        Package("Nose slider_Fixed.package", 1, Old, (CasPart, 100));
        Package(Path.Combine("CC", "Nose slider.package"), 2, New, (CasPart, 100));

        var proposal = Assert.Single(Analyze().Proposals);

        Assert.Equal(ResolutionKind.OlderVersion, proposal.Kind);
        Assert.EndsWith("Nose slider.package", proposal.Change.File.AbsolutePath);
    }

    [Fact]
    public void SingleItemContainedInMergedSetIsDisabled()
    {
        Package("Battery Hair.package", 1, New, (CasPart, 100), (CasPart, 101));
        Package("80s hair set MERGED.package", 2, Old, (CasPart, 100), (CasPart, 101), (CasPart, 102), (CasPart, 103));

        var proposal = Assert.Single(Analyze().Proposals);

        Assert.Equal(ResolutionKind.ContainedInOtherFile, proposal.Kind);
        Assert.EndsWith("Battery Hair.package", proposal.Change.File.AbsolutePath);
    }

    [Fact]
    public void OverrideRemovesOnlyContestedResourcesAndCanBeUndone()
    {
        string loser = Package("Feet default 1V.package", 1, Old, (SkinTone, 100), (CasPart, 200), (CasPart, 201));
        Package("Feet default 7V.package", 2, New, (SkinTone, 100), (CasPart, 300));
        var original = File.ReadAllBytes(loser);

        var proposals = Analyze().Proposals;
        var recommended = Assert.Single(proposals, p => p.IsRecommended);
        Assert.Single(proposals, p => !p.IsRecommended); // alternative: other side wins
        Assert.Equal(ResolutionAction.RemoveResources, recommended.Action);
        Assert.Equal(loser, recommended.Change.File.AbsolutePath);
        Assert.False(recommended.IsSafe);

        string id;
        using (var recorder = _journal.Begin("Konflikt lösen"))
        {
            id = recorder.Id;
            Assert.True(ConflictResolver.Apply(recommended, recorder).Success);
        }

        var remaining = DbpfReader.TryReadIndex(loser)!.Select(r => r.Key).ToList();
        Assert.Equal(new[] { new ResourceKey(CasPart, 0, 200), new ResourceKey(CasPart, 0, 201) }, remaining);
        Assert.Empty(Analyze().Report.Groups); // conflict gone, mod still installed

        Assert.True(_journal.Undo(id).Success);
        Assert.Equal(original, File.ReadAllBytes(loser));
    }

    [Fact]
    public void OverridePairsOnlyBestMatchingFiles()
    {
        // A string table shared by every package of two sets would pair every file with every other.
        for (int i = 0; i < 3; i++)
        {
            Package(Path.Combine("SetA", $"item{i}.package"), 1, Old, (0x220557DA, 1), (CasPart, (ulong)(1000 + i)));
            Package(Path.Combine("SetB", $"other{i}.package"), 2, New, (0x220557DA, 1), (CasPart, (ulong)(2000 + i)));
        }

        var recommended = Analyze().Proposals.Where(p => p.IsRecommended).ToList();

        Assert.True(recommended.Count <= 3, $"{recommended.Count} proposals for 3 file pairs");
    }

    [Fact]
    public void SimsStudioMergeManifestIsNotAConflict()
    {
        Package("SetA_MERGED.package", 1, null, (S4sManifest, 0), (CasPart, 1));
        Package("SetB_MERGED.package", 2, null, (S4sManifest, 0), (CasPart, 2));

        Assert.Empty(Analyze().Report.Groups);
    }

    [Fact]
    public void SafeProposalsCanBeAppliedInOneUndoableStep()
    {
        Package("A.package", 1, Old, (CasPart, 100));
        Package(Path.Combine("Dup", "A.package"), 2, New, (CasPart, 100));
        Package("Hair_NonHQ.package", 3, null, (CasPart, 200));
        Package("Hair_HQ.package", 4, null, (CasPart, 200));

        var (_, report, _) = Analyze();
        var safe = ConflictResolver.SafeProposals(report);
        Assert.Equal(2, safe.Count);

        string id;
        using (var recorder = _journal.Begin("Sichere Lösungen"))
        {
            id = recorder.Id;
            Assert.All(safe, p => Assert.True(ConflictResolver.Apply(p, recorder).Success));
        }
        Assert.Empty(Analyze().Report.Groups);

        _journal.Undo(id);
        Assert.Equal(2, Analyze().Report.Groups.Count);
    }
}
