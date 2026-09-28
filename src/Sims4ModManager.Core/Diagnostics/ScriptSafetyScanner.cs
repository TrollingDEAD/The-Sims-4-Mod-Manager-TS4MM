using System.IO.Compression;
using System.Text;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Diagnostics;

public enum ScriptRisk
{
    /// <summary>Worth knowing, common in legitimate mods (e.g. update checks over the network).</summary>
    Notice,
    /// <summary>Unusual for a Sims mod, should come from a trusted creator.</summary>
    Suspicious,
    /// <summary>Strong malware indicator (embedded programs, obfuscated code execution).</summary>
    Dangerous
}

public sealed record ScriptFinding(ScriptRisk Risk, string Description);

public sealed record ScriptScanResult(ModEntry Mod, ModFileInfo File, IReadOnlyList<ScriptFinding> Findings)
{
    public ScriptRisk? HighestRisk => Findings.Count == 0 ? null : Findings.Max(f => f.Risk);
}

/// <summary>
/// Static check of script mods (.ts4script) for patterns that Sims mods normally do not need -
/// the same idea as TwistedMexi's ModGuard after the malware incidents in the Sims community.
/// Python bytecode keeps module and function names as plain strings, so a byte search finds
/// imports like "subprocess" or "ctypes" without decompiling. The result is a warning, not a
/// verdict: some legitimate mods (update checkers, big frameworks) use a few of these.
/// </summary>
public static class ScriptSafetyScanner
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr", ".msi", ".com", ".pif", ".jar"
    };

    private static readonly (string Token, ScriptRisk Risk, string Description)[] Rules =
    {
        ("subprocess", ScriptRisk.Suspicious, L.T("kann andere Programme starten (subprocess)")),
        ("ShellExecute", ScriptRisk.Suspicious, L.T("kann andere Programme starten (ShellExecute)")),
        ("ctypes", ScriptRisk.Suspicious, L.T("ruft Windows-Systemfunktionen direkt auf (ctypes)")),
        ("winreg", ScriptRisk.Suspicious, L.T("liest oder schreibt die Windows-Registry (winreg)")),
        ("urlopen", ScriptRisk.Notice, L.T("greift auf das Internet zu (z. B. für Update-Prüfungen)")),
        ("urlretrieve", ScriptRisk.Suspicious, L.T("lädt Dateien aus dem Internet herunter")),
        ("socket", ScriptRisk.Notice, L.T("öffnet Netzwerkverbindungen")),
    };

    public static IReadOnlyList<ScriptScanResult> Scan(IEnumerable<ModEntry> mods)
    {
        var results = new List<ScriptScanResult>();
        foreach (var mod in mods)
        {
            foreach (var file in mod.Files.Where(f => f.Kind == ModFileKind.Script))
            {
                var findings = ScanArchive(file.AbsolutePath);
                if (findings.Count > 0)
                    results.Add(new ScriptScanResult(mod, file, findings));
            }
        }
        return results.OrderByDescending(r => r.HighestRisk).ToList();
    }

    public static IReadOnlyList<ScriptFinding> ScanArchive(string path)
    {
        var findings = new List<ScriptFinding>();
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var tokensFound = new HashSet<string>(StringComparer.Ordinal);
            bool decodes = false, executes = false;

            foreach (var entry in archive.Entries)
            {
                string ext = Path.GetExtension(entry.FullName);
                if (ExecutableExtensions.Contains(ext))
                    findings.Add(new ScriptFinding(ScriptRisk.Dangerous, L.F("enthält die ausführbare Datei „{0}“ – Sims-Mods brauchen so etwas nie", entry.FullName)));

                if (!ext.Equals(".py", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".pyc", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.Length > 20_000_000)
                    continue;

                string content = ReadAsLatin1(entry);
                foreach (var (token, _, _) in Rules)
                    if (content.Contains(token, StringComparison.Ordinal))
                        tokensFound.Add(token);

                decodes |= content.Contains("b64decode", StringComparison.Ordinal) || content.Contains("marshal", StringComparison.Ordinal);
                executes |= content.Contains("exec", StringComparison.Ordinal) && content.Contains("compile", StringComparison.Ordinal);
            }

            foreach (var (token, risk, description) in Rules.Where(r => tokensFound.Contains(r.Token)))
                findings.Add(new ScriptFinding(risk, description));
            if (decodes && executes)
                findings.Add(new ScriptFinding(ScriptRisk.Dangerous, L.T("entschlüsselt Code zur Laufzeit und führt ihn aus – typisch für versteckten Schadcode")));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // unreadable archives are reported by the scanner elsewhere
        }
        return findings;
    }

    private static string ReadAsLatin1(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Encoding.Latin1.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }
}
