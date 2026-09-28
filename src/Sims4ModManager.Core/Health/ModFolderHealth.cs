using System.Text;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Health;

/// <summary>
/// Checks the Mods folder against the rules the game and the community troubleshooting guides
/// agree on: script mods at most one folder deep, packages at most five, no Sims 2/3 content,
/// no broken or half-downloaded files, a valid Resource.cfg. Clutter and broken files are moved to
/// a sibling folder "Mods (aussortiert)" instead of being deleted.
/// </summary>
public static class ModFolderHealth
{
    public const string SortedOutFolderName = "Mods (aussortiert)";
    public const int MaxScriptDepth = 1;
    public const int MaxPackageDepth = 5;
    public const int MaxPathLength = 259;

    /// <summary>The default Resource.cfg the game creates (packages up to five folders deep).</summary>
    public static readonly string[] StandardResourceCfgLines =
    {
        "Priority 500",
        "PackedFile *.package",
        "PackedFile */*.package",
        "PackedFile */*/*.package",
        "PackedFile */*/*/*.package",
        "PackedFile */*/*/*/*.package",
        "PackedFile */*/*/*/*/*.package",
    };

    private static readonly HashSet<string> ClutterExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".rtf", ".pdf", ".doc", ".docx", ".odt", ".md",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".url", ".htm", ".html", ".lnk"
    };

    private static readonly HashSet<string> ClutterNames = new(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", "desktop.ini", ".DS_Store" };

    private static readonly HashSet<string> IncompleteDownloadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".opdownload"
    };

    private static readonly HashSet<string> OtherGameExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sims3pack", ".sims2pack", ".s3pe", ".s2pe"
    };

    /// <summary>Other people's disable conventions ("Mod.packageOFF", "Mod.package.off" ...).</summary>
    private static readonly string[] ForeignDisabledSuffixes = { ".packageoff", ".package.off", ".package.bak", ".ts4scriptoff", ".ts4script.off" };

    public static IReadOnlyList<HealthIssue> Inspect(string modsPath)
    {
        var issues = new List<HealthIssue>();
        if (!Directory.Exists(modsPath))
            return issues;

        string sortedOut = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modsPath).TrimEnd('\\', '/'))!, SortedOutFolderName);
        var files = SafeFiles(modsPath);

        var wrongGame = new List<string>();
        var broken = new List<string>();
        var empty = new List<string>();
        var incomplete = new List<string>();
        var scriptTooDeep = new List<string>();
        var packageTooDeep = new List<string>();
        var longPaths = new List<string>();
        var clutter = new List<string>();
        var foreignDisabled = new List<string>();
        var specialChars = new List<string>();

        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            string lower = name.ToLowerInvariant();
            string ext = Path.GetExtension(name);
            var kind = ModFileNaming.ClassifyKind(name);
            int depth = Depth(modsPath, file);

            if (file.Length > MaxPathLength)
                longPaths.Add(file);
            if (Path.GetRelativePath(modsPath, file).Any(c => c > 127))
                specialChars.Add(file);

            if (OtherGameExtensions.Contains(ext)) { wrongGame.Add(file); continue; }
            if (IncompleteDownloadExtensions.Contains(ext)) { incomplete.Add(file); continue; }
            if (ForeignDisabledSuffixes.Any(s => lower.EndsWith(s, StringComparison.Ordinal))) { foreignDisabled.Add(file); continue; }
            if (ClutterNames.Contains(name) || ClutterExtensions.Contains(ext)) { clutter.Add(file); continue; }

            if (kind == Models.ModFileKind.Other)
                continue;

            long length = SafeLength(file);
            if (length == 0) { empty.Add(file); continue; }

            if (kind == Models.ModFileKind.Package)
            {
                switch (ClassifyPackage(file))
                {
                    case PackageGame.Sims2:
                    case PackageGame.Sims3:
                        wrongGame.Add(file);
                        continue;
                    case PackageGame.NotAPackage:
                        broken.Add(file);
                        continue;
                }
                if (depth > MaxPackageDepth)
                    packageTooDeep.Add(file);
            }
            else if (kind == Models.ModFileKind.Script && depth > MaxScriptDepth)
            {
                scriptTooDeep.Add(file);
            }
        }

        if (wrongGame.Count > 0)
            issues.Add(MoveOutIssue("wrong-game", HealthSeverity.Error, L.T("Inhalte für Die Sims 2/3"),
                L.F("{0} Datei(en) sind für Die Sims 2 oder 3 (anderes Package-Format bzw. .sims3pack). ", wrongGame.Count) +
                L.T("Solche Dateien lassen Sims 4 häufig schon vor dem Hauptmenü abstürzen."), wrongGame, modsPath, sortedOut, bulk: true));

        if (broken.Count > 0)
            issues.Add(MoveOutIssue("broken-package", HealthSeverity.Error, L.T("Beschädigte Packages"),
                L.F("{0} Datei(en) enden auf .package, sind aber keine gültigen Packages (beschädigt, falsch umbenannt oder unvollständig).", broken.Count),
                broken, modsPath, sortedOut, bulk: true));

        if (empty.Count > 0)
            issues.Add(MoveOutIssue("empty-file", HealthSeverity.Error, L.T("Leere Mod-Dateien"),
                L.F("{0} Mod-Datei(en) haben 0 Byte – der Download ist fehlgeschlagen. Bitte neu herunterladen.", empty.Count),
                empty, modsPath, sortedOut, bulk: true));

        if (incomplete.Count > 0)
            issues.Add(MoveOutIssue("incomplete-download", HealthSeverity.Warning, L.T("Abgebrochene Downloads"),
                L.F("{0} unvollständige Download-Datei(en) (.crdownload, .part …) liegen im Mods-Ordner. Der Download muss wiederholt werden.", incomplete.Count),
                incomplete, modsPath, sortedOut, bulk: true));

        if (scriptTooDeep.Count > 0)
            issues.Add(new HealthIssue
            {
                Id = "script-too-deep",
                Category = L.T("Ordnerstruktur"),
                Severity = HealthSeverity.Error,
                Title = L.T("Skript-Mods zu tief verschachtelt"),
                Description = L.F("{0} .ts4script-Datei(en) liegen tiefer als {1} Unterordner im Mods-Ordner – ", scriptTooDeep.Count, MaxScriptDepth) +
                              L.T("das Spiel lädt sie dort nicht. Sie werden in ihren obersten Unterordner verschoben."),
                Paths = scriptTooDeep,
                FixLabel = L.T("Nach oben verschieben"),
                IsSafeToFixInBulk = true,
                Fix = r => MoveUp(scriptTooDeep, modsPath, MaxScriptDepth, r)
            });

        if (packageTooDeep.Count > 0)
            issues.Add(new HealthIssue
            {
                Id = "package-too-deep",
                Category = L.T("Ordnerstruktur"),
                Severity = HealthSeverity.Error,
                Title = L.T("Packages zu tief verschachtelt"),
                Description = L.F("{0} .package-Datei(en) liegen tiefer als {1} Unterordner – das Spiel lädt sie nicht.", packageTooDeep.Count, MaxPackageDepth),
                Paths = packageTooDeep,
                FixLabel = L.T("Nach oben verschieben"),
                IsSafeToFixInBulk = true,
                Fix = r => MoveUp(packageTooDeep, modsPath, MaxPackageDepth, r)
            });

        if (longPaths.Count > 0)
            issues.Add(new HealthIssue
            {
                Id = "path-too-long",
                Category = L.T("Ordnerstruktur"),
                Severity = HealthSeverity.Warning,
                Title = L.T("Zu lange Dateipfade"),
                Description = L.F("{0} Pfad(e) sind länger als {1} Zeichen. Windows und das Spiel können solche Dateien oft nicht öffnen – ", longPaths.Count, MaxPathLength) +
                              L.T("Ordner- oder Dateinamen kürzen."),
                Paths = longPaths
            });

        if (foreignDisabled.Count > 0)
            issues.Add(new HealthIssue
            {
                Id = "foreign-disabled",
                Category = L.T("Dateien"),
                Severity = HealthSeverity.Info,
                Title = L.T("Anders deaktivierte Mods"),
                Description = L.F("{0} Mod(s) wurden per Hand deaktiviert (z. B. „.packageOFF“). Umbenennen nach „.disabled“, ", foreignDisabled.Count) +
                              L.T("damit sie hier in der Liste erscheinen und per Häkchen wieder aktiviert werden können."),
                Paths = foreignDisabled,
                FixLabel = L.T("In Liste übernehmen"),
                IsSafeToFixInBulk = true,
                Fix = r => AdoptForeignDisabled(foreignDisabled, r)
            });

        if (clutter.Count > 0)
            issues.Add(MoveOutIssue("clutter", HealthSeverity.Info, L.T("Überflüssige Dateien im Mods-Ordner"),
                L.F("{0} Datei(en) wie Readmes, Bilder oder Verknüpfungen werden vom Spiel nicht gebraucht. ", clutter.Count) +
                L.T("Sie werden nach „Mods (aussortiert)“ verschoben (nicht gelöscht)."), clutter, modsPath, sortedOut, bulk: true));

        var emptyDirs = EmptyDirectories(modsPath);
        if (emptyDirs.Count > 0)
            issues.Add(new HealthIssue
            {
                Id = "empty-folders",
                Category = L.T("Ordnerstruktur"),
                Severity = HealthSeverity.Info,
                Title = L.T("Leere Ordner"),
                Description = L.F("{0} leere Ordner im Mods-Ordner (Reste gelöschter Mods).", emptyDirs.Count),
                Paths = emptyDirs,
                FixLabel = L.T("Entfernen"),
                IsSafeToFixInBulk = true,
                Fix = r =>
                {
                    foreach (string dir in emptyDirs.OrderByDescending(d => d.Length))
                        r.DeleteEmptyDirectory(dir);
                    return HealthFixResult.From(emptyDirs.Count, new List<string>());
                }
            });

        if (specialChars.Count > 0)
            issues.Add(new HealthIssue
            {
                Id = "special-characters",
                Category = L.T("Dateien"),
                Severity = HealthSeverity.Info,
                Title = L.T("Sonderzeichen in Namen"),
                Description = L.F("{0} Pfad(e) enthalten Sonderzeichen (z. B. ♡, Umlaute). Meist unproblematisch – ", specialChars.Count) +
                              L.T("wenn einzelne Mods nicht laden, ist Umbenennen ein Versuch wert."),
                Paths = specialChars
            });

        issues.AddRange(CheckResourceCfg(modsPath));
        return issues;
    }

    // --- Resource.cfg ---------------------------------------------------------------------------

    private static IEnumerable<HealthIssue> CheckResourceCfg(string modsPath)
    {
        string path = Path.Combine(modsPath, "Resource.cfg");
        if (!File.Exists(path))
        {
            yield return new HealthIssue
            {
                Id = "resource-cfg-missing",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Error,
                Title = L.T("Resource.cfg fehlt"),
                Description = L.T("Ohne Resource.cfg lädt das Spiel keine Packages aus Unterordnern. Sie wird mit dem Standardinhalt neu angelegt."),
                Paths = new[] { path },
                FixLabel = L.T("Anlegen"),
                IsSafeToFixInBulk = true,
                Fix = r => WriteResourceCfg(path, StandardResourceCfgLines, r)
            };
            yield break;
        }

        var lines = File.ReadAllLines(path).Select(l => l.Trim()).ToList();
        var missing = StandardResourceCfgLines.Skip(1)
            .Where(req => !lines.Any(l => string.Equals(l, req, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (missing.Count > 0)
            yield return new HealthIssue
            {
                Id = "resource-cfg-incomplete",
                Category = L.T("Spiel"),
                Severity = HealthSeverity.Warning,
                Title = L.T("Resource.cfg unvollständig"),
                Description = L.F("In der Resource.cfg fehlen {0} Zeile(n) – Packages in manchen Unterordner-Ebenen werden nicht geladen. ", missing.Count) +
                              L.T("Die fehlenden Zeilen werden ergänzt, eigene Zeilen bleiben erhalten."),
                Paths = new[] { path },
                FixLabel = L.T("Ergänzen"),
                IsSafeToFixInBulk = true,
                Fix = r => WriteResourceCfg(path, File.ReadAllLines(path).Concat(missing).ToArray(), r)
            };
    }

    private static HealthFixResult WriteResourceCfg(string path, string[] lines, ChangeRecorder recorder)
    {
        string content = string.Join("\r\n", lines) + "\r\n";
        if (File.Exists(path))
        {
            recorder.Replace(path, temp => File.WriteAllText(temp, content, new UTF8Encoding(false)));
        }
        else
        {
            string temp = Path.Combine(Path.GetTempPath(), "s4mm-resource-" + Guid.NewGuid().ToString("N") + ".cfg");
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            try { recorder.CopyIn(temp, path); }
            finally { File.Delete(temp); }
        }
        return HealthFixResult.From(1, new List<string>());
    }

    // --- Fix helpers ------------------------------------------------------------------------------

    private static HealthIssue MoveOutIssue(string id, HealthSeverity severity, string title, string description,
        List<string> paths, string modsPath, string sortedOut, bool bulk) => new()
    {
        Id = id,
        Category = L.T("Dateien"),
        Severity = severity,
        Title = title,
        Description = description,
        Paths = paths,
        FixLabel = L.T("Aussortieren"),
        IsSafeToFixInBulk = bulk,
        Fix = r => MoveOut(paths, modsPath, sortedOut, r)
    };

    /// <summary>Moves files out of Mods, keeping their relative folder structure under <paramref name="sortedOut"/>.</summary>
    private static HealthFixResult MoveOut(IEnumerable<string> paths, string modsPath, string sortedOut, ChangeRecorder recorder)
    {
        int count = 0;
        var errors = new List<string>();
        foreach (string file in paths.Where(File.Exists))
        {
            string target = Tray.TrayHousekeeping.UniquePath(Path.Combine(sortedOut, Path.GetRelativePath(modsPath, file)));
            try { recorder.Move(file, target); count++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        return HealthFixResult.From(count, errors);
    }

    /// <summary>Moves files up so they are at most <paramref name="maxDepth"/> folders deep (keeping the top-level folders).</summary>
    private static HealthFixResult MoveUp(IEnumerable<string> paths, string modsPath, int maxDepth, ChangeRecorder recorder)
    {
        int count = 0;
        var errors = new List<string>();
        foreach (string file in paths.Where(File.Exists))
        {
            var segments = Path.GetRelativePath(modsPath, Path.GetDirectoryName(file)!).Split(Path.DirectorySeparatorChar);
            string targetDir = Path.Combine(new[] { modsPath }.Concat(segments.Take(maxDepth)).ToArray());
            string target = Path.Combine(targetDir, Path.GetFileName(file));
            if (File.Exists(target))
            {
                errors.Add(L.F("{0}: im Zielordner existiert bereits eine Datei mit diesem Namen.", Path.GetFileName(file)));
                continue;
            }
            try { recorder.Move(file, target); count++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        return HealthFixResult.From(count, errors);
    }

    private static HealthFixResult AdoptForeignDisabled(IEnumerable<string> paths, ChangeRecorder recorder)
    {
        int count = 0;
        var errors = new List<string>();
        foreach (string file in paths.Where(File.Exists))
        {
            string lower = file.ToLowerInvariant();
            string suffix = ForeignDisabledSuffixes.First(s => lower.EndsWith(s, StringComparison.Ordinal));
            string real = suffix.Contains("ts4script") ? ModFileNaming.ScriptExtension : ModFileNaming.PackageExtension;
            string target = file[..^suffix.Length] + real + ModFileNaming.DisabledSuffix;
            if (File.Exists(target))
            {
                errors.Add(L.F("{0}: {1} existiert bereits.", Path.GetFileName(file), Path.GetFileName(target)));
                continue;
            }
            try { recorder.Move(file, target); count++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        return HealthFixResult.From(count, errors);
    }

    // --- Detection helpers ------------------------------------------------------------------------

    internal enum PackageGame { Sims4, Sims3, Sims2, NotAPackage }

    /// <summary>DBPF 1.x = Sims 2, 2.0 = Sims 3, 2.1 = Sims 4.</summary>
    internal static PackageGame ClassifyPackage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[12];
            if (stream.Read(header, 0, 12) < 12 || Encoding.ASCII.GetString(header, 0, 4) != "DBPF")
                return PackageGame.NotAPackage;
            uint major = BitConverter.ToUInt32(header, 4);
            uint minor = BitConverter.ToUInt32(header, 8);
            return (major, minor) switch
            {
                (1, _) => PackageGame.Sims2,
                (2, 0) => PackageGame.Sims3,
                (2, _) => PackageGame.Sims4,
                _ => PackageGame.NotAPackage
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PackageGame.Sims4; // locked (game running): not our business here
        }
    }

    private static int Depth(string modsPath, string file) =>
        Path.GetRelativePath(modsPath, Path.GetDirectoryName(file)!)
            .Split(Path.DirectorySeparatorChar)
            .Count(s => s.Length > 0 && s != ".");

    private static List<string> EmptyDirectories(string root)
    {
        var result = new List<string>();
        try
        {
            foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            {
                if (!Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
                    result.Add(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // partial result is fine
        }
        return result;
    }

    private static string[] SafeFiles(string dir)
    {
        try { return Directory.GetFiles(dir, "*", SearchOption.AllDirectories); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return -1; }
    }
}
