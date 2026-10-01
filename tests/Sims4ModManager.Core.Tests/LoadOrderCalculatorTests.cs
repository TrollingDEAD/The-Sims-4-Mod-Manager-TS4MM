using Sims4ModManager.Core.LoadOrder;

namespace Sims4ModManager.Core.Tests;

public class LoadOrderCalculatorTests : IDisposable
{
    private readonly string _root;

    public LoadOrderCalculatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_LoadOrder_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Touch(string relativePath)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    private static bool AllFiles(string path) => File.Exists(path);

    [Fact]
    public void FlatFolderRanksAlphabetically()
    {
        string a = Touch("A.package");
        string b = Touch("B.package");
        string c = Touch("C.package");

        var ranks = LoadOrderCalculator.Rank(_root, AllFiles);

        Assert.Equal(0, ranks[a]);
        Assert.Equal(1, ranks[b]);
        Assert.Equal(2, ranks[c]);
    }

    [Fact]
    public void FolderThatSortsBeforeALooseFileIsVisitedFirst()
    {
        // "AFolder" < "B.package" alphabetically, so AFolder's contents load entirely before B.
        string inner = Touch(Path.Combine("AFolder", "Z.package"));
        string loose = Touch("B.package");

        var ranks = LoadOrderCalculator.Rank(_root, AllFiles);

        Assert.Equal(0, ranks[inner]);
        Assert.Equal(1, ranks[loose]);
    }

    [Fact]
    public void LooseFileThatSortsBeforeAFolderIsVisitedFirst()
    {
        // "Aloose.package" < "ZFolder" alphabetically, so it loads before descending into ZFolder -
        // this is the case that distinguishes the real algorithm from "folders always load first".
        string loose = Touch("Aloose.package");
        string inner = Touch(Path.Combine("ZFolder", "A.package"));

        var ranks = LoadOrderCalculator.Rank(_root, AllFiles);

        Assert.Equal(0, ranks[loose]);
        Assert.Equal(1, ranks[inner]);
    }

    [Fact]
    public void UntrackedEntriesAreSkippedButDoNotBreakNumbering()
    {
        string a = Touch("A.package");
        string readme = Touch("B_readme.txt");
        string c = Touch("C.package");

        var ranks = LoadOrderCalculator.Rank(_root, p => p.EndsWith(".package", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(0, ranks[a]);
        Assert.False(ranks.ContainsKey(readme));
        Assert.Equal(1, ranks[c]);
    }

    [Fact]
    public void NumericPrefixesSortInNumericOrder()
    {
        string first = Touch("000_Core.ts4script");
        string second = Touch("010_Gameplay.ts4script");
        string third = Touch("ZZZ_Override.package");

        var ranks = LoadOrderCalculator.Rank(_root, File.Exists);

        Assert.Equal(0, ranks[first]);
        Assert.Equal(1, ranks[second]);
        Assert.Equal(2, ranks[third]);
    }
}
