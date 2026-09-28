using System.Globalization;
using System.Text;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Export;

public enum ModListFormat
{
    /// <summary>Readable list for Discord, forums or support requests.</summary>
    Text,
    /// <summary>Spreadsheet (semicolon-separated, UTF-8 with BOM so Excel shows umlauts).</summary>
    Csv
}

/// <summary>Exports the installed mods - what support channels and tools like Mod Hound ask for first.</summary>
public static class ModListExporter
{
    public static string Render(IReadOnlyList<ModEntry> mods, ModListFormat format, string? gameVersion = null) =>
        format == ModListFormat.Csv ? RenderCsv(mods) : RenderText(mods, gameVersion);

    public static void Write(IReadOnlyList<ModEntry> mods, string path, ModListFormat format, string? gameVersion = null)
    {
        var encoding = format == ModListFormat.Csv ? new UTF8Encoding(true) : new UTF8Encoding(false);
        File.WriteAllText(path, Render(mods, format, gameVersion), encoding);
    }

    private static string RenderText(IReadOnlyList<ModEntry> mods, string? gameVersion)
    {
        var sb = new StringBuilder();
        var enabled = mods.Where(m => m.IsEnabled || m.IsPartiallyEnabled).ToList();
        var disabled = mods.Where(m => !m.IsEnabled && !m.IsPartiallyEnabled).ToList();
        var scripts = enabled.Where(m => m.ContainsScript).ToList();

        sb.AppendLine(L.T("Sims 4 – Mod-Liste"));
        sb.AppendLine(L.F("Erstellt: {0}", DateTime.Now.ToString("g", L.Culture)) +
                      (gameVersion is null ? "" : L.F(" · Spielversion {0}", gameVersion)));
        sb.AppendLine(L.F("{0} Mods ({1} aktiv, {2} deaktiviert), davon {3} mit Skripten, {4} gesamt",
            mods.Count, enabled.Count, disabled.Count, scripts.Count, FormatSize(mods.Sum(m => m.TotalSizeBytes))));
        sb.AppendLine();

        if (scripts.Count > 0)
        {
            sb.AppendLine(L.F("Skript-Mods ({0}):", scripts.Count));
            foreach (var mod in scripts)
                sb.AppendLine($"  - {Describe(mod)}");
            sb.AppendLine();
        }

        var others = enabled.Where(m => !m.ContainsScript).ToList();
        sb.AppendLine(scripts.Count > 0
            ? L.F("Weitere aktive Mods & CC ({0}):", others.Count)
            : L.F("Aktive Mods & CC ({0}):", others.Count));
        foreach (var mod in others)
            sb.AppendLine($"  - {Describe(mod)}");

        if (disabled.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(L.F("Deaktiviert ({0}):", disabled.Count));
            foreach (var mod in disabled)
                sb.AppendLine($"  - {Describe(mod)}");
        }
        return sb.ToString();
    }

    private static string Describe(ModEntry mod)
    {
        string files = mod.IsFolder ? L.F(", {0} Dateien", mod.Files.Count) : "";
        string partial = mod.IsPartiallyEnabled ? L.T(", teilweise aktiv") : "";
        return $"{mod.DisplayName} ({FormatSize(mod.TotalSizeBytes)}, {mod.LastModifiedUtc.ToLocalTime():d}{files}{partial})";
    }

    private static string RenderCsv(IReadOnlyList<ModEntry> mods)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.T("Name;Status;Art;Inhalt;Dateien;Größe (Bytes);Geändert;Pfad"));
        foreach (var mod in mods)
        {
            string status = mod.IsEnabled ? L.T("aktiv") : mod.IsPartiallyEnabled ? L.T("teilweise") : L.T("deaktiviert");
            string content = (mod.ContainsPackage, mod.ContainsScript) switch
            {
                (true, true) => L.T("Package + Skript"),
                (true, false) => L.T("Package"),
                (false, true) => L.T("Skript"),
                _ => "-"
            };
            sb.AppendLine(string.Join(';',
                Csv(mod.DisplayName), status, mod.IsFolder ? L.T("Ordner") : L.T("Datei"), content,
                mod.Files.Count.ToString(CultureInfo.InvariantCulture),
                mod.TotalSizeBytes.ToString(CultureInfo.InvariantCulture),
                mod.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Csv(mod.AbsolutePath)));
        }
        return sb.ToString();
    }

    private static string Csv(string value) =>
        value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return string.Create(CultureInfo.CurrentCulture, $"{size:0.#} {units[unit]}");
    }
}
