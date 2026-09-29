using System.Collections.Concurrent;
using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Persistence;
using Sims4ModManager.Core.Tray;

namespace Sims4ModManager.Core.Online;

/// <summary>A CurseForge project installed in the Mods folder, with the file installed and the newest file.</summary>
public sealed class InstalledCurseForgeMod
{
    public required long ModId { get; init; }
    public required string Name { get; init; }
    public string? WebsiteUrl { get; init; }
    public string Authors { get; init; } = string.Empty;
    public required CurseForgeFile InstalledFile { get; init; }
    public CurseForgeFile? LatestFile { get; init; }
    public required IReadOnlyList<(ModEntry Mod, ModFileInfo File)> LocalFiles { get; init; }

    /// <summary>False if the author only allows downloads on the CurseForge website.</summary>
    public bool AllowsDownload { get; init; } = true;

    public bool UpdateAvailable => LatestFile is not null && LatestFile.Id != InstalledFile.Id && LatestFile.FileDate > InstalledFile.FileDate;
}

public sealed record UpdateCheckResult(IReadOnlyList<InstalledCurseForgeMod> Mods, int CheckedFiles, int UnmatchedFiles);

/// <summary>
/// Finds the Mods folder's CurseForge projects by file fingerprint and compares them with the newest
/// release. Fingerprints are cached by path, size and date - the first check reads every file once.
/// </summary>
public sealed class CurseForgeUpdateChecker
{
    private sealed class CacheEntry
    {
        public string Key { get; set; } = string.Empty;
        public long Size { get; set; }
        public long Ticks { get; set; }
        public uint Fingerprint { get; set; }
    }

    private readonly CurseForgeClient _client;
    private readonly string _cacheFile;

    public CurseForgeUpdateChecker(CurseForgeClient client, string? cacheRoot = null)
    {
        _client = client;
        _cacheFile = Path.Combine(cacheRoot ?? AppPaths.Cache, "fingerprints.json");
    }

    public async Task<UpdateCheckResult> CheckAsync(IReadOnlyList<ModEntry> mods, IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancel = default)
    {
        var files = mods.SelectMany(m => m.Files.Where(f => f.Kind != ModFileKind.Other).Select(f => (Mod: m, File: f))).ToList();
        var fingerprints = await Task.Run(() => ComputeFingerprints(files.Select(f => f.File).ToList(), progress, cancel), cancel);

        var byFingerprint = files.GroupBy(f => fingerprints[f.File]).ToDictionary(g => g.Key, g => g.ToList());
        var matches = await _client.MatchFingerprintsAsync(byFingerprint.Keys.ToList(), cancel);

        // Which local files each matched CurseForge file accounts for (whole file, or a file inside its archive).
        var perProject = new Dictionary<long, List<(CurseForgeMatch Match, List<(ModEntry, ModFileInfo)> Local)>>();
        foreach (var match in matches.ExactMatches.Concat(matches.PartialMatches))
        {
            var local = new[] { match.File.FileFingerprint }.Concat(match.File.Modules.Select(m => m.Fingerprint))
                .Distinct()
                .Where(byFingerprint.ContainsKey)
                .SelectMany(fp => byFingerprint[fp])
                .Select(x => (x.Mod, x.File))
                .ToList();
            if (local.Count == 0)
                continue;
            if (!perProject.TryGetValue(match.Id, out var list))
                perProject[match.Id] = list = new();
            list.Add((match, local));
        }

        var projects = perProject.Count == 0
            ? new Dictionary<long, CurseForgeMod>()
            : (await _client.GetModsAsync(perProject.Keys.ToList(), cancel)).ToDictionary(m => m.Id);

        var result = new List<InstalledCurseForgeMod>();
        foreach (var (modId, entries) in perProject)
        {
            var installed = entries.Select(e => e.Match.File).OrderByDescending(f => f.FileDate).First();
            projects.TryGetValue(modId, out var project);
            var candidates = (project?.LatestFiles ?? new List<CurseForgeFile>())
                .Concat(entries.SelectMany(e => e.Match.LatestFiles))
                .Where(f => f.IsAvailable)
                .ToList();
            var latest = candidates.Where(f => f.ReleaseType == 1).OrderByDescending(f => f.FileDate).FirstOrDefault()
                         ?? candidates.OrderByDescending(f => f.FileDate).FirstOrDefault();
            result.Add(new InstalledCurseForgeMod
            {
                ModId = modId,
                Name = project?.Name ?? installed.DisplayName,
                WebsiteUrl = project?.Links.WebsiteUrl,
                Authors = string.Join(", ", project?.Authors.Select(a => a.Name) ?? Enumerable.Empty<string>()),
                InstalledFile = installed,
                LatestFile = latest,
                LocalFiles = entries.SelectMany(e => e.Local).Distinct().ToList(),
                AllowsDownload = project?.AllowModDistribution != false
            });
        }

        var matchedFiles = result.SelectMany(r => r.LocalFiles).Select(l => l.File).ToHashSet();
        return new UpdateCheckResult(
            result.OrderByDescending(r => r.UpdateAvailable).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            files.Count, files.Count(f => !matchedFiles.Contains(f.File)));
    }

    private Dictionary<ModFileInfo, uint> ComputeFingerprints(IReadOnlyList<ModFileInfo> files, IProgress<(int, int)>? progress, CancellationToken cancel)
    {
        var cache = (JsonFile.TryRead<List<CacheEntry>>(_cacheFile) ?? new List<CacheEntry>())
            .GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.Last());
        var result = new ConcurrentDictionary<ModFileInfo, uint>();
        var fresh = new ConcurrentBag<CacheEntry>();
        int done = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancel }, file =>
        {
            string key = ModFileNaming.ToEnabledPath(file.AbsolutePath).ToLowerInvariant();
            if (cache.TryGetValue(key, out var hit) && hit.Size == file.SizeBytes && hit.Ticks == file.LastWriteUtc.Ticks)
            {
                result[file] = hit.Fingerprint;
            }
            else
            {
                try
                {
                    uint fp = CurseForgeFingerprint.Compute(file.AbsolutePath);
                    result[file] = fp;
                    fresh.Add(new CacheEntry { Key = key, Size = file.SizeBytes, Ticks = file.LastWriteUtc.Ticks, Fingerprint = fp });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result[file] = 0; // unreadable: cannot match
                }
            }
            int now = Interlocked.Increment(ref done);
            if (now % 100 == 0 || now == files.Count)
                progress?.Report((now, files.Count));
        });

        var live = files.Select(f => ModFileNaming.ToEnabledPath(f.AbsolutePath).ToLowerInvariant()).ToHashSet();
        var entries = cache.Values.Where(e => live.Contains(e.Key)).Concat(fresh).GroupBy(e => e.Key).Select(g => g.Last()).ToList();
        try { JsonFile.WriteAtomic(_cacheFile, entries); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* only an accelerator */ }
        return new Dictionary<ModFileInfo, uint>(result);
    }

    /// <summary>
    /// Replaces the installed files of a project with a downloaded update (a mod file or an archive of
    /// them) in the folder of the old files. Old files go into the change set, so the update can be undone.
    /// Returns the installed file paths.
    /// </summary>
    public static IReadOnlyList<string> ApplyUpdate(ChangeRecorder recorder, IReadOnlyList<string> oldFiles, string download, string downloadName)
    {
        if (oldFiles.Count == 0)
            throw new InvalidOperationException("No installed files to replace.");
        string staging = StagingDir();
        try
        {
            var newFiles = ExtractModFiles(download, downloadName, staging);

            string targetDir = Path.GetDirectoryName(oldFiles[0])!;
            bool disabled = oldFiles.All(f => ModFileNaming.IsDisabled(f));
            foreach (string old in oldFiles.Where(File.Exists))
                recorder.Delete(old);

            var installed = new List<string>();
            foreach (var (source, name) in newFiles.DistinctBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                string target = Path.Combine(targetDir, name);
                if (disabled)
                    target = ModFileNaming.ToDisabledPath(target);
                recorder.CopyIn(source, target);
                installed.Add(target);
            }
            return installed;
        }
        finally { CleanupStaging(staging); }
    }

    /// <summary>
    /// Installs a downloaded file (or archive of them) fresh into <paramref name="targetDir"/> - no old
    /// files to replace, used by the CurseForge Browse tab's install/dependency pipeline. Returns the
    /// installed file paths.
    /// </summary>
    public static IReadOnlyList<string> ApplyInstall(ChangeRecorder recorder, string targetDir, string download, string downloadName)
    {
        string staging = StagingDir();
        try
        {
            var newFiles = ExtractModFiles(download, downloadName, staging);
            var installed = new List<string>();
            foreach (var (source, name) in newFiles.DistinctBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                string target = Path.Combine(targetDir, name);
                recorder.CopyIn(source, target);
                installed.Add(target);
            }
            return installed;
        }
        finally { CleanupStaging(staging); }
    }

    /// <summary>Extracts a downloaded file/archive into <paramref name="staging"/> and returns every managed mod file found.</summary>
    private static List<(string Source, string Name)> ExtractModFiles(string download, string downloadName, string staging)
    {
        var newFiles = new List<(string Source, string Name)>();
        if (TrayInstaller.IsArchive(downloadName))
        {
            var warnings = new List<string>();
            if (!TrayInstaller.TryExtract(download, staging, downloadName, warnings))
                throw new IOException(warnings.FirstOrDefault() ?? L.F("{0} ließ sich nicht entpacken.", downloadName));
            newFiles.AddRange(Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .Where(f => ModFileNaming.IsManagedModFile(Path.GetFileName(f)))
                .Select(f => (f, Path.GetFileName(f))));
        }
        else if (ModFileNaming.IsManagedModFile(downloadName))
        {
            newFiles.Add((download, downloadName));
        }
        if (newFiles.Count == 0)
            throw new IOException(L.F("{0} enthält keine Mod-Dateien.", downloadName));
        return newFiles;
    }

    private static string StagingDir() => Path.Combine(Path.GetTempPath(), "Sims4ModManager", "update-" + Guid.NewGuid().ToString("N"));

    private static void CleanupStaging(string staging)
    {
        try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp folder */ }
    }
}
