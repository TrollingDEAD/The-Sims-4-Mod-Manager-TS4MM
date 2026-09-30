using System.Text;
using Sims4ModManager.Core.Conflicts;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Export;

/// <summary>
/// Exports the current conflict report (every group, its affected mods/resources, and the
/// resolver's recommended fix) as a shareable document - for asking the community for help with a
/// conflict the in-app resolver doesn't safely auto-fix, the way the mod list is already exportable.
/// </summary>
public static class ConflictReportExporter
{
    public static string Render(ConflictReport report, IReadOnlyList<ResolutionProposal> proposals, ModListFormat format) =>
        format == ModListFormat.Csv ? RenderCsv(report, proposals) : RenderText(report, proposals);

    public static void Write(ConflictReport report, IReadOnlyList<ResolutionProposal> proposals, string path, ModListFormat format)
    {
        var encoding = format == ModListFormat.Csv ? new UTF8Encoding(true) : new UTF8Encoding(false);
        File.WriteAllText(path, Render(report, proposals, format), encoding);
    }

    private static string? Recommendation(ConflictGroup group, IReadOnlyList<ResolutionProposal> proposals) =>
        ConflictResolver.ForGroup(proposals, group).FirstOrDefault(p => p.IsRecommended)?.Title;

    private static string RenderText(ConflictReport report, IReadOnlyList<ResolutionProposal> proposals)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.T("Sims 4 – Konfliktbericht"));
        sb.AppendLine(L.F("Erstellt: {0}", DateTime.Now.ToString("g", L.Culture)));
        sb.AppendLine(L.F("{0} Konfliktgruppe(n), {1} betroffene Ressource(n).", report.Groups.Count, report.TotalItemCount));
        sb.AppendLine();

        foreach (var group in report.Groups.OrderByDescending(g => g.Severity))
        {
            sb.AppendLine(string.Join(" ↔ ", group.Mods.Select(m => m.DisplayName)) + " " +
                          L.F("({0} Konflikt(e))", group.Items.Count));
            sb.AppendLine("  " + L.F("Schwere: {0}", SeverityLabel(group.Severity)));
            foreach (var item in group.Items)
                sb.AppendLine("  " + L.F("- {0}: {1} ({2})", item.TypeName, item.Label, string.Join(", ", item.Sources.Select(ConflictResolver.Label))));
            if (Recommendation(group, proposals) is { } recommendation)
                sb.AppendLine("  " + L.F("Vorschlag: {0}", recommendation));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string RenderCsv(ConflictReport report, IReadOnlyList<ResolutionProposal> proposals)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.T("Mods;Schwere;Typ;Ressource;Dateien;Vorschlag"));
        foreach (var group in report.Groups.OrderByDescending(g => g.Severity))
        {
            string mods = string.Join(" ↔ ", group.Mods.Select(m => m.DisplayName));
            string recommendation = Recommendation(group, proposals) ?? "";
            foreach (var item in group.Items)
            {
                sb.AppendLine(string.Join(';',
                    Csv(mods), SeverityLabel(group.Severity), Csv(item.TypeName), Csv(item.Label),
                    Csv(string.Join(", ", item.Sources.Select(ConflictResolver.Label))), Csv(recommendation)));
            }
        }
        return sb.ToString();
    }

    private static string SeverityLabel(ConflictSeverity severity) => severity switch
    {
        ConflictSeverity.High => L.T("Hoch"),
        ConflictSeverity.Medium => L.T("Mittel"),
        _ => L.T("Niedrig")
    };

    private static string Csv(string value) =>
        value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
