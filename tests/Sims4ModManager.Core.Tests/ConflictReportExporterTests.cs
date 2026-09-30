using Sims4ModManager.Core.Conflicts;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Export;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Tests;

public class ConflictReportExporterTests : IDisposable
{
    private const uint CasPart = 0x034AEECB;

    private readonly string _root;
    private readonly string _mods;

    public ConflictReportExporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_ConflictExport_" + Guid.NewGuid());
        _mods = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(_mods);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Package(string relativePath, int variant, DateTime lastWrite, params (uint Type, ulong Instance)[] resources)
    {
        string path = Path.Combine(_mods, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var entries = resources
            .Select(r => new TestDbpfBuilder.Entry(r.Type, 0, r.Instance, BitConverter.GetBytes(r.Instance * 31 + (ulong)variant)))
            .ToList();
        File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));
        File.SetLastWriteTimeUtc(path, lastWrite);
        return path;
    }

    private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private (ConflictReport Report, IReadOnlyList<ResolutionProposal> Proposals) Analyze()
    {
        var mods = ModScanner.Scan(_mods);
        var report = ConflictDetector.FindConflicts(mods);
        return (report, ConflictResolver.ProposeAll(report));
    }

    [Fact]
    public void TextReportListsGroupsItemsAndRecommendation()
    {
        Package("Loose.package", 1, Old, (CasPart, 100), (CasPart, 101));
        Package(Path.Combine("Set", "Loose.package"), 2, New, (CasPart, 100), (CasPart, 101));
        var (report, proposals) = Analyze();

        string text = ConflictReportExporter.Render(report, proposals, ModListFormat.Text);

        Assert.Contains("Loose.package", text);
        Assert.Contains("CAS-Teil", text);
        Assert.Contains("Vorschlag:", text);
    }

    [Fact]
    public void CsvReportHasOneRowPerItem()
    {
        Package("Loose.package", 1, Old, (CasPart, 100), (CasPart, 101));
        Package(Path.Combine("Set", "Loose.package"), 2, New, (CasPart, 100), (CasPart, 101));
        var (report, proposals) = Analyze();

        string csv = ConflictReportExporter.Render(report, proposals, ModListFormat.Csv);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        int totalItems = report.Groups.Sum(g => g.Items.Count);
        Assert.Equal(totalItems + 1, lines.Length); // header + one row per conflict item
    }

    [Fact]
    public void EmptyReportRendersWithoutGroups()
    {
        string text = ConflictReportExporter.Render(ConflictReport.Empty, Array.Empty<ResolutionProposal>(), ModListFormat.Text);
        Assert.Contains("0 Konfliktgruppe", text);
    }
}
