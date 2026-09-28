using System.IO.Compression;

namespace Sims4ModManager.Core.Tests;

/// <summary>Scanner + readers + detector against real files on disk.</summary>
public class ConflictEndToEndTests : IDisposable
{
    private readonly string _modsRoot;

    public ConflictEndToEndTests()
    {
        _modsRoot = Path.Combine(Path.GetTempPath(), "S4MM_Conflicts_" + Guid.NewGuid());
        Directory.CreateDirectory(_modsRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_modsRoot))
            Directory.Delete(_modsRoot, recursive: true);
    }

    private void WritePackage(string name, params TestDbpfBuilder.Entry[] entries) =>
        File.WriteAllBytes(Path.Combine(_modsRoot, name), TestDbpfBuilder.Build(entries));

    private void WriteScript(string name, params (string Entry, string Content)[] files)
    {
        using var archive = ZipFile.Open(Path.Combine(_modsRoot, name), ZipArchiveMode.Create);
        foreach (var (entry, content) in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write(content);
        }
    }

    [Fact]
    public void OnlyDifferingResourcesAreReportedEvenWhenCompressedDifferently()
    {
        byte[] shared = Enumerable.Repeat((byte)42, 500).ToArray();
        WritePackage("ModA.package",
            new TestDbpfBuilder.Entry(0x034AEECB, 0, 1, shared),
            new TestDbpfBuilder.Entry(0x034AEECB, 0, 2, new byte[] { 1, 2, 3 }));
        WritePackage("ModB.package",
            new TestDbpfBuilder.Entry(0x034AEECB, 0, 1, shared, TestDbpfBuilder.Zlib),
            new TestDbpfBuilder.Entry(0x034AEECB, 0, 2, new byte[] { 9, 9, 9 }));

        var report = ConflictDetector.FindConflicts(ModScanner.Scan(_modsRoot));

        var item = Assert.Single(Assert.Single(report.Groups).Items);
        Assert.Equal(2UL, item.Key!.Value.Instance);
        Assert.Equal(1, report.IgnoredIdenticalCount);
    }

    [Fact]
    public void DetectsConflictingScriptModules()
    {
        WriteScript("ModA.ts4script", ("shared/util.py", "print('a')"), ("moda/main.py", "x = 1"));
        WriteScript("ModB.ts4script", ("shared/util.py", "print('b')"), ("modb/main.py", "x = 1"));

        var report = ConflictDetector.FindConflicts(ModScanner.Scan(_modsRoot));

        var item = Assert.Single(Assert.Single(report.Groups).Items);
        Assert.Equal("shared/util", item.Label);
    }

    [Fact]
    public void ReportsCorruptFilesAsUnreadable()
    {
        File.WriteAllBytes(Path.Combine(_modsRoot, "Broken.package"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(_modsRoot, "Broken.ts4script"), new byte[] { 1, 2, 3 });

        var report = ConflictDetector.FindConflicts(ModScanner.Scan(_modsRoot));

        Assert.Equal(2, report.UnreadableFiles.Count);
    }
}
