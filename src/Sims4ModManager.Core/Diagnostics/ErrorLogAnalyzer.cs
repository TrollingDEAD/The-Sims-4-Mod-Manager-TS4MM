using System.Net;
using System.Text.RegularExpressions;
using Sims4ModManager.Core.Game;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Diagnostics;

public enum ErrorLogKind
{
    /// <summary>Python script error (lastException) - most often caused by script mods.</summary>
    ScriptException,
    /// <summary>User interface error (lastUIException).</summary>
    UiException,
    /// <summary>Game crash (lastCrash) - only native addresses, no mod assignment possible.</summary>
    Crash,
    /// <summary>Report written by a mod (MC Command Center, Better Exceptions).</summary>
    ModReport
}

/// <summary>A mod file the error points to, and why.</summary>
public sealed record ErrorSuspect(ModEntry Mod, ModFileInfo File, string Reason);

/// <summary>One error from a log file (a file can contain several).</summary>
public sealed class ErrorLogEntry
{
    public required string FilePath { get; init; }
    public required ErrorLogKind Kind { get; init; }
    public DateTime? CreatedLocal { get; init; }
    public string? GameVersion { get; init; }
    public required string Category { get; init; }
    public required string Details { get; init; }
    public IReadOnlyList<string> ModuleHints { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ErrorSuspect> Suspects { get; init; } = Array.Empty<ErrorSuspect>();

    /// <summary>Script archives named in the traceback that are no longer installed (error probably solved by removing them).</summary>
    public IReadOnlyList<string> MissingScripts { get; init; } = Array.Empty<string>();

    /// <summary>Written by an older game version than the installed one - usually resolved by the update or a mod update.</summary>
    public bool IsFromOlderGameVersion { get; init; }

    /// <summary>One line summary of the error message (the exception text).</summary>
    public string Headline => Details.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? Category;
}

/// <summary>Errors with the same category (same place in the code), newest first.</summary>
public sealed record ErrorLogGroup(string Category, ErrorLogKind Kind, IReadOnlyList<ErrorLogEntry> Entries)
{
    public ErrorLogEntry Latest => Entries[0];
    public int Count => Entries.Count;
}

/// <summary>
/// Reads the game's error logs (lastException/lastUIException/lastCrash*.txt - XML with HTML-encoded
/// Python tracebacks) and mod-written reports (MCCC's mc_lastexception.html, Better Exceptions),
/// and points to the responsible script mod where possible: via mod paths in the traceback or
/// Python module names that match a module inside an installed .ts4script.
/// </summary>
public static class ErrorLogAnalyzer
{
    private static readonly string[] GameLogPatterns = { "lastException*.txt", "lastUIException*.txt", "lastCrash*.txt" };
    private static readonly string[] ModReportPatterns = { "mc_lastexception*.html", "*BE-ExceptionReport*.html", "*BetterExceptions*.html" };

    private static readonly Regex ReportPattern = new(@"<report>(?<body>.*?)</report>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new(@"\d+\.\d+\.\d+\.\d+", RegexOptions.Compiled);
    private static readonly Regex TracebackFile = new(@"File ""(?<path>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex PyModule = new(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\.pyc?\b", RegexOptions.Compiled);

    /// <summary>All log files in the game folder and (for mod reports) the Mods folder.</summary>
    public static IReadOnlyList<string> FindLogFiles(string gameDataFolder, string? modsPath)
    {
        var files = new List<string>();
        foreach (string pattern in GameLogPatterns)
            files.AddRange(SafeFiles(gameDataFolder, pattern, SearchOption.TopDirectoryOnly));
        foreach (string pattern in ModReportPatterns)
        {
            files.AddRange(SafeFiles(gameDataFolder, pattern, SearchOption.TopDirectoryOnly));
            if (modsPath is not null)
                files.AddRange(SafeFiles(modsPath, pattern, SearchOption.AllDirectories));
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<ErrorLogEntry> Analyze(string gameDataFolder, string? modsPath, IReadOnlyList<ModEntry> mods)
    {
        string? currentVersion = GameInfo.TryReadGameVersion(gameDataFolder);
        var moduleIndex = BuildModuleIndex(mods);
        var entries = new List<ErrorLogEntry>();

        foreach (string file in FindLogFiles(gameDataFolder, modsPath))
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                entries.Add(ParseModReport(file, text, currentVersion, moduleIndex, mods));
            else
                entries.AddRange(ParseGameLog(file, text, currentVersion, moduleIndex, mods));
        }

        return entries.OrderByDescending(e => e.CreatedLocal ?? File.GetLastWriteTime(e.FilePath)).ToList();
    }

    /// <summary>Groups identical errors (same category), newest group first.</summary>
    public static IReadOnlyList<ErrorLogGroup> Group(IEnumerable<ErrorLogEntry> entries) =>
        entries.GroupBy(e => (e.Kind, e.Category))
            .Select(g => new ErrorLogGroup(g.Key.Category, g.Key.Kind,
                g.OrderByDescending(e => e.CreatedLocal ?? DateTime.MinValue).ToList()))
            .OrderByDescending(g => g.Latest.CreatedLocal ?? DateTime.MinValue)
            .ToList();

    private static IEnumerable<ErrorLogEntry> ParseGameLog(string file, string text, string? currentVersion,
        Dictionary<string, List<(ModEntry, ModFileInfo)>> moduleIndex, IReadOnlyList<ModEntry> mods)
    {
        string name = Path.GetFileName(file);
        var kind = name.StartsWith("lastCrash", StringComparison.OrdinalIgnoreCase) ? ErrorLogKind.Crash
            : name.StartsWith("lastUIException", StringComparison.OrdinalIgnoreCase) ? ErrorLogKind.UiException
            : ErrorLogKind.ScriptException;

        var reports = ReportPattern.Matches(text).Select(m => m.Groups["body"].Value).ToList();
        if (reports.Count == 0)
            reports.Add(text);

        foreach (string report in reports)
        {
            string category = WebUtility.HtmlDecode(Tag(report, "categoryid") ?? "").Trim();
            string details = kind == ErrorLogKind.Crash
                ? L.T("Das Spiel ist abgestürzt. Der Bericht enthält nur Speicheradressen – ein verursachender Mod lässt sich daraus nicht ablesen. ") +
                  L.T("Häufige Ursachen: veraltete Skript-Mods, beschädigte Packages, Grafiktreiber. Der 50/50-Assistent hilft beim Eingrenzen.") +
                  CrashSeparator + CleanDetails(Regex.Replace(report, "<[^>]+>", "\n"))
                : CleanDetails(Tag(report, "desyncdata") ?? "");
            string? build = Tag(report, "buildsignature");
            string? version = build is null ? null : VersionPattern.Match(build) is { Success: true } m ? m.Value : null;

            yield return Build(file, kind, category.Length > 0 ? category : L.T("(unbekannt)"), details, version,
                ParseDate(Tag(report, "createtime")), currentVersion, moduleIndex, mods);
        }
    }

    private static ErrorLogEntry ParseModReport(string file, string text, string? currentVersion,
        Dictionary<string, List<(ModEntry, ModFileInfo)>> moduleIndex, IReadOnlyList<ModEntry> mods)
    {
        string plain = CleanDetails(Regex.Replace(text, "<[^>]+>", "\n"));
        string version = VersionPattern.Match(plain) is { Success: true } m ? m.Value : null!;
        string category = plain.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase) || l.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                          ?? Path.GetFileName(file);
        return Build(file, ErrorLogKind.ModReport, category.Length > 120 ? category[..120] : category, plain, version,
            File.GetLastWriteTime(file), currentVersion, moduleIndex, mods);
    }

    private static ErrorLogEntry Build(string file, ErrorLogKind kind, string category, string details, string? version,
        DateTime? created, string? currentVersion, Dictionary<string, List<(ModEntry, ModFileInfo)>> moduleIndex, IReadOnlyList<ModEntry> mods)
    {
        var (hints, suspects, missing) = FindSuspects(category + "\n" + details, moduleIndex, mods);
        return new ErrorLogEntry
        {
            FilePath = file,
            Kind = kind,
            CreatedLocal = created,
            GameVersion = version,
            Category = category,
            Details = details,
            ModuleHints = hints,
            Suspects = suspects,
            MissingScripts = missing,
            IsFromOlderGameVersion = version is not null && currentVersion is not null && GameInfo.CompareVersions(version, currentVersion) < 0
        };
    }

    /// <summary>
    /// Finds mods named by the error: (1) paths inside the Mods folder in the traceback, (2) Python
    /// modules that are not part of the game (game code lives under T:\InGame\... or core/simulation)
    /// matched against modules of installed script mods.
    /// </summary>
    private static (IReadOnlyList<string> Hints, IReadOnlyList<ErrorSuspect> Suspects, IReadOnlyList<string> Missing) FindSuspects(string text,
        Dictionary<string, List<(ModEntry Mod, ModFileInfo File)>> moduleIndex, IReadOnlyList<ModEntry> mods)
    {
        var suspects = new List<ErrorSuspect>();
        var hints = new List<string>();
        var missing = new List<string>();

        foreach (Match match in TracebackFile.Matches(text))
        {
            string path = match.Groups["path"].Value;
            if (path.StartsWith(@"T:\InGame", StringComparison.OrdinalIgnoreCase))
                continue; // game code

            int scriptAt = path.IndexOf(".ts4script", StringComparison.OrdinalIgnoreCase);
            if (scriptAt > 0)
            {
                string archive = Path.GetFileName(path[..(scriptAt + ".ts4script".Length)]);
                int before = suspects.Count;
                foreach (var mod in mods)
                    foreach (var f in mod.Files.Where(f => string.Equals(ModFileNaming.GetEffectiveFileName(Path.GetFileName(f.AbsolutePath)), archive, StringComparison.OrdinalIgnoreCase)))
                        suspects.Add(new ErrorSuspect(mod, f, L.F("Im Fehlerbericht steht der Pfad „{0}“.", archive)));
                if (suspects.Count == before)
                    missing.Add(archive);
            }

            string module = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (module.Length > 0)
                hints.Add(module);
        }

        foreach (Match match in PyModule.Matches(text))
            hints.Add(match.Groups["name"].Value.ToLowerInvariant());

        foreach (string module in hints.Distinct())
        {
            if (!moduleIndex.TryGetValue(module, out var owners))
                continue;
            foreach (var (mod, file) in owners)
                suspects.Add(new ErrorSuspect(mod, file, L.F("Das Python-Modul „{0}“ aus dem Fehler stammt aus diesem Skript-Mod.", module)));
        }

        return (hints.Distinct().Where(moduleIndex.ContainsKey).ToList(),
            suspects.DistinctBy(s => s.File.AbsolutePath, StringComparer.OrdinalIgnoreCase).ToList(),
            missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Module file name (last path segment, lower case) → script files providing it.</summary>
    private static Dictionary<string, List<(ModEntry, ModFileInfo)>> BuildModuleIndex(IReadOnlyList<ModEntry> mods)
    {
        var index = new Dictionary<string, List<(ModEntry, ModFileInfo)>>(StringComparer.Ordinal);
        foreach (var mod in mods)
        {
            foreach (var file in mod.Files.Where(f => f.Kind == ModFileKind.Script))
            {
                foreach (var module in file.ScriptModules)
                {
                    string name = module.ModulePath.Split('/').Last();
                    if (name is "__init__" or "main" or "utils" or "settings")
                        continue; // too generic to point at one mod
                    if (!index.TryGetValue(name, out var list))
                        index[name] = list = new List<(ModEntry, ModFileInfo)>();
                    list.Add((mod, file));
                }
            }
        }
        return index;
    }

    private static string? Tag(string xml, string tag)
    {
        var match = Regex.Match(xml, $"<{tag}>(?<v>.*?)</{tag}>", RegexOptions.Singleline);
        return match.Success ? match.Groups["v"].Value : null;
    }

    /// <summary>Separates the explanation of a crash from the raw report in <see cref="ErrorLogEntry.Details"/>.</summary>
    public const string CrashSeparator = "\n\n";

    private static string CleanDetails(string raw)
    {
        string decoded = WebUtility.HtmlDecode(WebUtility.HtmlDecode(raw));
        decoded = decoded.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = decoded.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0);
        return string.Join("\n", lines);
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var date) ? date : null;

    private static IEnumerable<string> SafeFiles(string dir, string pattern, SearchOption option)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, option) : Array.Empty<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
