using System.Security.Cryptography;
using SharpCompress.Archives;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Tray;

public enum InstallTargetKind
{
    Tray,
    Mods,
    Ignored
}

public enum InstallStatus
{
    New,
    /// <summary>An identical file already exists at the target (or elsewhere in Mods) - nothing to do.</summary>
    AlreadyInstalled,
    /// <summary>A different file with the same name exists at the target - skipped unless overwriting.</summary>
    Conflict
}

public sealed class InstallEntry
{
    public required string SourceLabel { get; init; }
    public required InstallTargetKind Kind { get; init; }
    public string? TargetPath { get; init; }
    public required InstallStatus Status { get; init; }
    public string? Note { get; init; }
    public long SizeBytes { get; init; }

    /// <summary>Extracted copy of the file (for archive sources) or the original file.</summary>
    internal string? StagedPath { get; init; }
}

/// <summary>
/// Everything a download (archive, folder or loose files) would install, computed before anything
/// is copied so the user can review it. Archives are unpacked into a private staging folder that
/// is deleted when the plan is disposed.
/// </summary>
public sealed class InstallPlan : IDisposable
{
    public required IReadOnlyList<InstallEntry> Entries { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    internal string? StagingDirectory { get; init; }

    public int Count(InstallTargetKind kind, InstallStatus status) =>
        Entries.Count(e => e.Kind == kind && e.Status == status);

    public bool NothingToDo => Entries.All(e => e.Kind == InstallTargetKind.Ignored || e.Status != InstallStatus.New)
                               && !Entries.Any(e => e.Status == InstallStatus.Conflict);

    public void Dispose()
    {
        if (StagingDirectory is not null && Directory.Exists(StagingDirectory))
        {
            try { Directory.Delete(StagingDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp folder, best effort */ }
        }
    }
}

public sealed record InstallResult(int Installed, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// Installs Sims 4 downloads the way the game expects them: tray files flat into Tray (never into
/// subfolders, never renamed), .package/.ts4script into Mods (one subfolder per download, at most
/// one level deep so script mods still load). Supports .zip, .rar and .7z archives, including
/// archives nested in archives, plain folders and loose files.
/// </summary>
public static class TrayInstaller
{
    public static readonly IReadOnlySet<string> ArchiveExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };

    private const int MaxArchiveNesting = 3;

    public static bool IsArchive(string path) => ArchiveExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// True for mod/tray files and for archives that contain any (without extracting them) -
    /// so a downloaded "photos.zip" does not trigger an install prompt.
    /// </summary>
    public static bool LooksLikeSimsDownload(string path)
    {
        string name = Path.GetFileName(path);
        if (ModFileNaming.IsManagedModFile(name) || TrayFileName.HasTrayExtension(name))
            return true;
        if (!IsArchive(path))
            return false;
        try
        {
            using var archive = ArchiveFactory.OpenArchive(path, new SharpCompress.Readers.ReaderOptions());
            return archive.Entries.Any(e => !e.IsDirectory && e.Key is { } key &&
                (ModFileNaming.IsManagedModFile(Path.GetFileName(key)) || TrayFileName.HasTrayExtension(key) || IsArchive(key)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    public static InstallPlan Analyze(IEnumerable<string> sources, string trayPath, string modsPath)
    {
        string staging = Path.Combine(Path.GetTempPath(), "Sims4ModManager", "install-" + Guid.NewGuid().ToString("N"));
        var collected = new List<(string SourceLabel, string SourceName, string FilePath)>();
        var warnings = new List<string>();

        foreach (string source in sources)
        {
            string sourceName = SanitizeFolderName(Path.GetFileNameWithoutExtension(source.TrimEnd('\\', '/')));
            if (Directory.Exists(source))
            {
                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    Collect(file, $"{Path.GetFileName(source)} › {Path.GetRelativePath(source, file)}", sourceName, 0);
            }
            else if (File.Exists(source))
            {
                Collect(source, Path.GetFileName(source), IsArchive(source) ? sourceName : "", 0);
            }
            else
            {
                warnings.Add(L.F("Nicht gefunden: {0}", source));
            }
        }

        void Collect(string file, string label, string sourceName, int depth)
        {
            if (!IsArchive(file))
            {
                collected.Add((label, sourceName, file));
                return;
            }

            if (depth >= MaxArchiveNesting)
            {
                warnings.Add(L.F("{0}: zu tief verschachteltes Archiv – übersprungen.", label));
                return;
            }

            string extractDir = Path.Combine(staging, collected.Count + "-" + Guid.NewGuid().ToString("N")[..8]);
            if (!TryExtract(file, extractDir, label, warnings))
                return;

            foreach (string extracted in Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories))
            {
                string innerRelative = Path.GetRelativePath(extractDir, extracted);
                Collect(extracted, $"{label} › {innerRelative}", sourceName, depth + 1);
            }
        }

        var existingMods = IndexExistingModFiles(modsPath);
        var entries = new List<InstallEntry>();
        var plannedTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // target -> staged path

        foreach (var (label, sourceName, file) in collected)
        {
            string fileName = Path.GetFileName(file);
            long size = new FileInfo(file).Length;

            if (TrayFileName.HasTrayExtension(fileName))
            {
                string target = Path.Combine(trayPath, fileName);
                string? note = TrayFileName.TryParse(fileName, out _)
                    ? null
                    : L.T("Dateiname entspricht nicht dem Schema des Spiels (umbenannt?) – wird evtl. nicht erkannt.");
                entries.Add(CreateEntry(label, InstallTargetKind.Tray, file, target, size, note, plannedTargets, existing: null));
            }
            else if (ModFileNaming.IsManagedModFile(fileName))
            {
                string effectiveName = ModFileNaming.GetEffectiveFileName(fileName);
                string folder = sourceName.Length == 0 ? modsPath : Path.Combine(modsPath, sourceName);
                string target = Path.Combine(folder, effectiveName);
                existingMods.TryGetValue(effectiveName, out var sameName);
                entries.Add(CreateEntry(label, InstallTargetKind.Mods, file, target, size,
                    ModFileNaming.IsDisabled(fileName) ? L.T("War im Download deaktiviert – wird aktiviert installiert.") : null,
                    plannedTargets, sameName));
            }
            else
            {
                entries.Add(new InstallEntry
                {
                    SourceLabel = label,
                    Kind = InstallTargetKind.Ignored,
                    Status = InstallStatus.New,
                    SizeBytes = size,
                    Note = L.T("Keine Sims 4-Datei (z. B. Bild oder Anleitung) – wird nicht installiert.")
                });
            }
        }

        warnings.AddRange(CheckTrayCompleteness(entries, trayPath));

        // Check downloaded script mods before they get installed.
        foreach (var script in entries.Where(e => e.Kind == InstallTargetKind.Mods && e.StagedPath is not null
                                                  && e.StagedPath.EndsWith(ModFileNaming.ScriptExtension, StringComparison.OrdinalIgnoreCase)))
        {
            var findings = Diagnostics.ScriptSafetyScanner.ScanArchive(script.StagedPath!);
            var serious = findings.Where(f => f.Risk >= Diagnostics.ScriptRisk.Suspicious).ToList();
            if (serious.Count > 0)
                warnings.Add(L.F("⚠ {0} {1} – nur installieren, wenn der Mod aus einer vertrauenswürdigen Quelle stammt.", Path.GetFileName(script.StagedPath), string.Join(", ", serious.Select(f => f.Description))));
        }

        return new InstallPlan
        {
            Entries = entries,
            Warnings = warnings,
            StagingDirectory = staging
        };
    }

    /// <summary>
    /// Copies the plan's new files. With a <paramref name="recorder"/> the installation is journaled:
    /// undo removes the installed files and restores anything that was overwritten.
    /// </summary>
    public static InstallResult Execute(InstallPlan plan, bool overwriteConflicts = false, Backup.ChangeRecorder? recorder = null)
    {
        int installed = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var entry in plan.Entries)
        {
            bool shouldCopy = entry.Kind != InstallTargetKind.Ignored
                              && (entry.Status == InstallStatus.New || (entry.Status == InstallStatus.Conflict && overwriteConflicts));
            if (!shouldCopy)
            {
                if (entry.Kind != InstallTargetKind.Ignored)
                    skipped++;
                continue;
            }

            try
            {
                if (recorder is not null)
                {
                    recorder.CopyIn(entry.StagedPath!, entry.TargetPath!);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.TargetPath!)!);
                    string temp = entry.TargetPath + ".installing";
                    File.Copy(entry.StagedPath!, temp, overwrite: true);
                    File.Move(temp, entry.TargetPath!, overwrite: true);
                }
                installed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{entry.SourceLabel}: {ex.Message}");
            }
        }

        return new InstallResult(installed, skipped, errors);
    }

    private static InstallEntry CreateEntry(string label, InstallTargetKind kind, string stagedPath, string target, long size,
        string? note, Dictionary<string, string> plannedTargets, List<string>? existing)
    {
        var status = InstallStatus.New;

        if (plannedTargets.TryGetValue(target, out var otherStaged))
        {
            // Two files of the download map to the same target.
            if (FilesEqual(otherStaged, stagedPath))
                return new InstallEntry { SourceLabel = label, Kind = kind, TargetPath = target, StagedPath = stagedPath, SizeBytes = size,
                    Status = InstallStatus.AlreadyInstalled, Note = L.T("Doppelt im Download enthalten.") };

            // Tray files must keep their name, so a clash cannot be resolved by renaming.
            if (kind == InstallTargetKind.Tray)
                return new InstallEntry { SourceLabel = label, Kind = kind, TargetPath = target, StagedPath = stagedPath, SizeBytes = size,
                    Status = InstallStatus.Conflict, Note = L.T("Zwei verschiedene Tray-Dateien mit gleichem Namen im Download – übersprungen.") };

            target = MakeUnique(target, plannedTargets);
        }

        if (File.Exists(target))
        {
            status = FilesEqual(target, stagedPath) ? InstallStatus.AlreadyInstalled : InstallStatus.Conflict;
            if (status == InstallStatus.Conflict)
                note = L.T("Eine andere Datei mit diesem Namen existiert bereits – wird nicht überschrieben.");
        }
        else if (existing is not null)
        {
            string? identical = existing.FirstOrDefault(p => FilesEqual(p, stagedPath));
            if (identical is not null)
            {
                status = InstallStatus.AlreadyInstalled;
                note = ModFileNaming.IsDisabled(identical)
                    ? L.F("Bereits vorhanden, aber deaktiviert: {0}", identical)
                    : L.F("Bereits vorhanden: {0}", identical);
            }
        }

        plannedTargets[target] = stagedPath;
        return new InstallEntry
        {
            SourceLabel = label,
            Kind = kind,
            TargetPath = target,
            StagedPath = stagedPath,
            Status = status,
            Note = note,
            SizeBytes = size
        };
    }

    /// <summary>Warns about tray items that will still be incomplete after installing.</summary>
    private static IEnumerable<string> CheckTrayCompleteness(List<InstallEntry> entries, string trayPath)
    {
        var instances = new Dictionary<ulong, HashSet<string>>();
        void Add(string fileName)
        {
            if (!TrayFileName.TryParse(fileName, out var name))
                return;
            if (!instances.TryGetValue(name.Instance, out var set))
                instances[name.Instance] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(name.Extension);
        }

        var planned = entries.Where(e => e.Kind == InstallTargetKind.Tray).Select(e => Path.GetFileName(e.TargetPath!)).ToList();
        foreach (var fileName in planned)
            Add(fileName);

        var relevant = instances.Keys.ToHashSet();
        if (Directory.Exists(trayPath))
        {
            foreach (string existing in Directory.EnumerateFiles(trayPath))
            {
                if (TrayFileName.TryParse(Path.GetFileName(existing), out var name) && relevant.Contains(name.Instance))
                    Add(Path.GetFileName(existing));
            }
        }

        foreach (var (instance, extensions) in instances)
        {
            bool isPortraitOnly = extensions.All(e => e == "sgi");
            if (isPortraitOnly)
                continue; // portraits belong to a household with a different id

            if (!extensions.Contains(TrayFileName.TrayItemExtension))
                yield return L.F("Tray-Eintrag 0x{0:x16}: .trayitem-Datei fehlt – erscheint nicht in der Bibliothek.", instance);
            else if (extensions.Contains("hhi") && !extensions.Contains("householdbinary"))
                yield return L.F("Haushalt 0x{0:x16}: .householdbinary-Datei fehlt.", instance);
            else if (extensions.Contains("bpi") && !extensions.Contains("blueprint"))
                yield return L.F("Grundstück 0x{0:x16}: .blueprint-Datei fehlt.", instance);
            else if (extensions.Contains("rmi") && !extensions.Contains("room"))
                yield return L.F("Raum 0x{0:x16}: .room-Datei fehlt.", instance);
        }
    }

    internal static bool TryExtract(string archivePath, string targetDir, string label, List<string> warnings)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath, new SharpCompress.Readers.ReaderOptions());
            if (archive.IsEncrypted || archive.Entries.Any(e => e.IsEncrypted))
            {
                warnings.Add(L.F("{0}: Archiv ist passwortgeschützt – bitte manuell entpacken.", label));
                return false;
            }

            Directory.CreateDirectory(targetDir);
            string root = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;

            void Write(string? key, Func<Stream> open)
            {
                if (key is null)
                    return;
                string destination = Path.GetFullPath(Path.Combine(targetDir, key));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return; // path traversal ("../") in a malicious archive

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = open();
                using var output = File.Create(destination);
                input.CopyTo(output);
            }

            // Solid archives compress all files as one stream: opening entries individually would
            // decompress from the start every time, so read them sequentially instead.
            var sequential = archive switch
            {
                SharpCompress.Archives.SevenZip.SevenZipArchive sevenZip when archive.IsSolid => sevenZip.ExtractAllEntries(),
                SharpCompress.Archives.Rar.RarArchive rar when archive.IsSolid => rar.ExtractAllEntries(),
                _ => null
            };

            if (sequential is not null)
            {
                using (sequential)
                {
                    while (sequential.MoveToNextEntry())
                    {
                        if (!sequential.Entry.IsDirectory)
                            Write(sequential.Entry.Key, sequential.OpenEntryStream);
                    }
                }
                return true;
            }

            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                Write(entry.Key, entry.OpenEntryStream);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            warnings.Add(L.F("{0}: Archiv konnte nicht gelesen werden ({1}).", label, ex.Message));
            return false;
        }
    }

    /// <summary>Existing mod files in Mods by effective file name, to spot downloads that are already installed elsewhere.</summary>
    private static Dictionary<string, List<string>> IndexExistingModFiles(string modsPath)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(modsPath))
            return index;

        foreach (string file in Directory.EnumerateFiles(modsPath, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (!ModFileNaming.IsManagedModFile(name))
                continue;
            string effective = ModFileNaming.GetEffectiveFileName(name);
            if (!index.TryGetValue(effective, out var list))
                index[effective] = list = new List<string>();
            list.Add(file);
        }
        return index;
    }

    internal static bool FilesEqual(string a, string b)
    {
        try
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (infoA.Length != infoB.Length)
                return false;

            using var streamA = File.OpenRead(a);
            using var streamB = File.OpenRead(b);
            return SHA256.HashData(streamA).AsSpan().SequenceEqual(SHA256.HashData(streamB));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string MakeUnique(string target, Dictionary<string, string> planned)
    {
        string dir = Path.GetDirectoryName(target)!;
        string name = Path.GetFileNameWithoutExtension(target);
        string ext = Path.GetExtension(target);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!planned.ContainsKey(candidate) && !File.Exists(candidate))
                return candidate;
        }
    }

    internal static string SanitizeFolderName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "Download" : name;
    }
}
