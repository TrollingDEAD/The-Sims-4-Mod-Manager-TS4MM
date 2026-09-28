using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Tests;

public class ConflictDetectorTests
{
    private const uint CasPartType = 0x034AEECB;
    private const uint CasThumbnailType = 0x3C1AF1F2;

    /// <summary>A resource whose "content" is identified by <paramref name="contentId"/> (see <see cref="FakeHasher"/>).</summary>
    private static PackageResource Res(ResourceKey key, uint contentId = 0) =>
        new(key, ChunkOffset: 0, StoredSize: 0, MemorySize: contentId, CompressionType: 0);

    /// <summary>Uses MemorySize as a stand-in content hash so tests don't need real files.</summary>
    private static readonly ResourceHasher FakeHasher = (_, resources, _) =>
        resources.ToDictionary(r => r, r => (string?)r.MemorySize.ToString());

    private static readonly ResourceHasher UnreadableHasher = (_, resources, _) =>
        resources.ToDictionary(r => r, _ => (string?)null);

    private static ModFileInfo PackageFile(string path, bool enabled, params PackageResource[] resources) => new()
    {
        AbsolutePath = path,
        RelativePathInMod = Path.GetFileName(path),
        Kind = ModFileKind.Package,
        IsEnabled = enabled,
        SizeBytes = 0,
        Resources = resources
    };

    private static ModFileInfo ScriptFile(string path, params ScriptModule[] modules) => new()
    {
        AbsolutePath = path,
        RelativePathInMod = Path.GetFileName(path),
        Kind = ModFileKind.Script,
        IsEnabled = true,
        SizeBytes = 0,
        ScriptModules = modules
    };

    private static ModEntry Mod(string name, params ModFileInfo[] files) => new()
    {
        Id = name.ToLowerInvariant(),
        DisplayName = name,
        AbsolutePath = name,
        IsFolder = false,
        Files = files,
        LastModifiedUtc = DateTime.UtcNow
    };

    [Fact]
    public void FlagsSameKeyWithDifferentContentAcrossTwoDistinctMods()
    {
        var key = new ResourceKey(1, 2, 3);
        var modA = Mod("ModA", PackageFile("a.package", true, Res(key, 1)));
        var modB = Mod("ModB", PackageFile("b.package", true, Res(key, 2)));

        var report = ConflictDetector.FindConflicts(new[] { modA, modB }, FakeHasher);

        var group = Assert.Single(report.Groups);
        Assert.Equal(new[] { modA, modB }, group.Mods);
        var item = Assert.Single(group.Items);
        Assert.Equal(key, item.Key);
    }

    [Fact]
    public void DoesNotFlagDuplicateKeyWithinTheSameMod()
    {
        var key = new ResourceKey(1, 2, 3);
        var mod = Mod("MultiFileMod",
            PackageFile("a.package", true, Res(key, 1)),
            PackageFile("b.package", true, Res(key, 2)));

        var report = ConflictDetector.FindConflicts(new[] { mod }, FakeHasher);

        Assert.Empty(report.Groups);
    }

    [Fact]
    public void IgnoresDisabledFiles()
    {
        var key = new ResourceKey(1, 2, 3);
        var modA = Mod("ModA", PackageFile("a.package", true, Res(key, 1)));
        var modB = Mod("ModB", PackageFile("b.package", false, Res(key, 2)));

        var report = ConflictDetector.FindConflicts(new[] { modA, modB }, FakeHasher);

        Assert.Empty(report.Groups);
    }

    [Fact]
    public void PartiallyEnabledModStillConflictsThroughItsEnabledFiles()
    {
        var key = new ResourceKey(1, 2, 3);
        var modA = Mod("ModA", PackageFile("a.package", true, Res(key, 1)));
        var modB = Mod("ModB",
            PackageFile("b1.package", true, Res(key, 2)),
            PackageFile("b2.package", false));

        var report = ConflictDetector.FindConflicts(new[] { modA, modB }, FakeHasher);

        Assert.Single(report.Groups);
    }

    [Fact]
    public void IgnoresByteIdenticalDuplicatesAcrossMods()
    {
        var key = new ResourceKey(1, 2, 3);
        var modA = Mod("ModA", PackageFile("a.package", true, Res(key, 7)));
        var modB = Mod("ModB", PackageFile("b.package", true, Res(key, 7)));

        var report = ConflictDetector.FindConflicts(new[] { modA, modB }, FakeHasher);

        Assert.Empty(report.Groups);
        Assert.Equal(1, report.IgnoredIdenticalCount);
    }

    [Fact]
    public void TreatsUnreadableContentAsConflict()
    {
        var key = new ResourceKey(1, 2, 3);
        var modA = Mod("ModA", PackageFile("a.package", true, Res(key, 7)));
        var modB = Mod("ModB", PackageFile("b.package", true, Res(key, 7)));

        var report = ConflictDetector.FindConflicts(new[] { modA, modB }, UnreadableHasher);

        Assert.Single(report.Groups);
    }

    [Fact]
    public void GroupsConflictsBySetOfInvolvedMods()
    {
        var shared1 = new ResourceKey(1, 0, 1);
        var shared2 = new ResourceKey(1, 0, 2);
        var sharedByThree = new ResourceKey(1, 0, 3);

        var modA = Mod("ModA", PackageFile("a.package", true, Res(shared1, 1), Res(shared2, 1), Res(sharedByThree, 1)));
        var modB = Mod("ModB", PackageFile("b.package", true, Res(shared1, 2), Res(shared2, 2), Res(sharedByThree, 2)));
        var modC = Mod("ModC", PackageFile("c.package", true, Res(sharedByThree, 3)));

        var report = ConflictDetector.FindConflicts(new[] { modA, modB, modC }, FakeHasher);

        Assert.Equal(2, report.Groups.Count);
        Assert.Equal(3, report.TotalItemCount);

        var pair = Assert.Single(report.Groups, g => g.Mods.Count == 2);
        Assert.Equal(2, pair.Items.Count);
        var triple = Assert.Single(report.Groups, g => g.Mods.Count == 3);
        Assert.Equal(sharedByThree, Assert.Single(triple.Items).Key);
    }

    [Fact]
    public void ClassifiesResourceTypesAndOrdersGroupsBySeverity()
    {
        var thumbnail = new ResourceKey(CasThumbnailType, 0, 1);
        var casPart = new ResourceKey(CasPartType, 0, 2);

        var modA = Mod("ModA", PackageFile("a.package", true, Res(thumbnail, 1)));
        var modB = Mod("ModB", PackageFile("b.package", true, Res(thumbnail, 2)));
        var modC = Mod("ModC", PackageFile("c.package", true, Res(casPart, 1)));
        var modD = Mod("ModD", PackageFile("d.package", true, Res(casPart, 2)));

        var report = ConflictDetector.FindConflicts(new[] { modA, modB, modC, modD }, FakeHasher);

        Assert.Equal(2, report.Groups.Count);
        Assert.Equal(ConflictSeverity.High, report.Groups[0].Severity);
        Assert.Equal("CAS-Teil", report.Groups[0].Items[0].TypeName);
        Assert.Equal(ConflictSeverity.Low, report.Groups[1].Severity);
    }

    [Fact]
    public void FlagsScriptModulesThatDifferAndIgnoresIdenticalOnes()
    {
        var modA = Mod("ModA", ScriptFile("a.ts4script",
            new ScriptModule("lib/shared", "lib/shared.pyc", 0x1111, 10),
            new ScriptModule("lib/same", "lib/same.pyc", 0x2222, 20)));
        var modB = Mod("ModB", ScriptFile("b.ts4script",
            new ScriptModule("lib/shared", "lib/shared.py", 0x3333, 30),
            new ScriptModule("lib/same", "lib/same.pyc", 0x2222, 20)));

        var report = ConflictDetector.FindConflicts(new[] { modA, modB }, FakeHasher);

        var item = Assert.Single(Assert.Single(report.Groups).Items);
        Assert.Equal(ConflictItemKind.ScriptModule, item.Kind);
        Assert.Equal("lib/shared", item.Label);
        Assert.Equal(ConflictSeverity.High, item.Severity);
        Assert.Equal(1, report.IgnoredIdenticalCount);
    }

    [Fact]
    public void ReportsUnreadableEnabledFiles()
    {
        var broken = new ModFileInfo
        {
            AbsolutePath = "broken.package",
            RelativePathInMod = "broken.package",
            Kind = ModFileKind.Package,
            IsEnabled = true,
            SizeBytes = 0,
            IsUnreadable = true
        };
        var mod = Mod("Broken", broken);

        var report = ConflictDetector.FindConflicts(new[] { mod }, FakeHasher);

        var unreadable = Assert.Single(report.UnreadableFiles);
        Assert.Same(broken, unreadable.File);
    }
}
