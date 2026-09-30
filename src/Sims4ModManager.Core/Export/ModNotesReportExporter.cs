using System.Text;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Export;

/// <summary>
/// Exports every mod's own notes, tags, "why installed" reason, group and links - the full
/// "why is this here" record <see cref="ModListExporter"/> deliberately leaves out, for people who
/// maintain a curated pack across machines and want their own documentation to travel with the
/// list rather than just the file names.
/// </summary>
public static class ModNotesReportExporter
{
    public static string Render(IReadOnlyList<ModEntry> mods, IReadOnlyDictionary<string, ModNote> notes, ModListFormat format) =>
        format == ModListFormat.Csv ? RenderCsv(mods, notes) : RenderText(mods, notes);

    public static void Write(IReadOnlyList<ModEntry> mods, IReadOnlyDictionary<string, ModNote> notes, string path, ModListFormat format)
    {
        var encoding = format == ModListFormat.Csv ? new UTF8Encoding(true) : new UTF8Encoding(false);
        File.WriteAllText(path, Render(mods, notes, format), encoding);
    }

    private static List<(ModEntry Mod, ModNote Note)> Annotated(IReadOnlyList<ModEntry> mods, IReadOnlyDictionary<string, ModNote> notes) =>
        mods.Select(m => (Mod: m, Note: notes.TryGetValue(m.Id, out var n) ? n : null))
            .Where(x => x.Note is { IsEmpty: false })
            .Select(x => (x.Mod, x.Note!))
            .OrderBy(x => x.Mod.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string RenderText(IReadOnlyList<ModEntry> mods, IReadOnlyDictionary<string, ModNote> notes)
    {
        var annotated = Annotated(mods, notes);
        var sb = new StringBuilder();
        sb.AppendLine(L.T("Sims 4 – Notizen, Tags & Gründe"));
        sb.AppendLine(L.F("Erstellt: {0}", DateTime.Now.ToString("g", L.Culture)));
        sb.AppendLine(L.F("{0} von {1} Mods haben eigene Notizen, Tags, einen Grund oder eine Gruppe.", annotated.Count, mods.Count));
        sb.AppendLine();

        foreach (var (mod, note) in annotated)
        {
            sb.AppendLine((note.IsFavorite ? "★ " : "") + mod.DisplayName);
            if (!string.IsNullOrWhiteSpace(note.Note))
                sb.AppendLine("  " + L.F("Notiz: {0}", note.Note));
            if (!string.IsNullOrWhiteSpace(note.Reason))
                sb.AppendLine("  " + L.F("Warum installiert: {0}", note.Reason));
            if (note.Tags.Count > 0)
                sb.AppendLine("  " + L.F("Tags: {0}", string.Join(", ", note.Tags)));
            if (!string.IsNullOrWhiteSpace(note.Group))
                sb.AppendLine("  " + L.F("Gruppe: {0}", note.Group));
            if (!string.IsNullOrWhiteSpace(note.DownloadUrl))
                sb.AppendLine("  " + L.F("Download: {0}", note.DownloadUrl));
            if (!string.IsNullOrWhiteSpace(note.CreatorUrl))
                sb.AppendLine("  " + L.F("Ersteller: {0}", note.CreatorUrl));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string RenderCsv(IReadOnlyList<ModEntry> mods, IReadOnlyDictionary<string, ModNote> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.T("Name;Favorit;Notiz;Warum installiert;Tags;Gruppe;Download-Link;Ersteller-Link"));
        foreach (var (mod, note) in Annotated(mods, notes))
        {
            sb.AppendLine(string.Join(';',
                Csv(mod.DisplayName), note.IsFavorite ? L.T("Ja") : "",
                Csv(note.Note ?? ""), Csv(note.Reason ?? ""), Csv(string.Join(", ", note.Tags)),
                Csv(note.Group ?? ""), Csv(note.DownloadUrl ?? ""), Csv(note.CreatorUrl ?? "")));
        }
        return sb.ToString();
    }

    private static string Csv(string value) =>
        value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
