using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.Core.Backup;

public sealed record TrayMirrorResult(int Copied, int Archived, IReadOnlyList<string> Errors);

/// <summary>
/// Incremental backup of the Tray folder (library): a mirror in backups\tray\Aktuell that only copies new
/// or changed files. Overwritten and deleted items are moved to "Ältere Stände\&lt;Datum&gt;" first and
/// kept for a number of days - so a household replaced or deleted by mistake can be recovered without
/// re-zipping gigabytes before every game start.
/// </summary>
public sealed class TrayMirror
{
    public TrayMirror(string? root = null)
    {
        Root = root ?? Path.Combine(AppPaths.Root, "backups", "tray");
    }

    public string Root { get; }
    public string CurrentFolder => Path.Combine(Root, "Aktuell");
    public string OlderFolder => Path.Combine(Root, "Ältere Stände");

    public bool Exists => Directory.Exists(CurrentFolder);

    /// <summary>Bytes the next update will copy (all of the Tray folder on the first run).</summary>
    public long PendingBytes(string trayFolder)
    {
        if (!Directory.Exists(trayFolder))
            return 0;
        return Directory.GetFiles(trayFolder).Select(f => new FileInfo(f))
            .Where(f => !IsUnchanged(f, Path.Combine(CurrentFolder, f.Name)))
            .Sum(f => f.Length);
    }

    public TrayMirrorResult Update(string trayFolder, int keepDays = 30)
    {
        var errors = new List<string>();
        int copied = 0, archived = 0;
        if (!Directory.Exists(trayFolder))
            return new TrayMirrorResult(0, 0, errors);

        Directory.CreateDirectory(CurrentFolder);
        string archive = Path.Combine(OlderFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in Directory.GetFiles(trayFolder).Select(f => new FileInfo(f)))
        {
            present.Add(source.Name);
            string target = Path.Combine(CurrentFolder, source.Name);
            if (IsUnchanged(source, target))
                continue;
            try
            {
                if (File.Exists(target))
                {
                    Directory.CreateDirectory(archive);
                    File.Move(target, Path.Combine(archive, source.Name));
                    archived++;
                }
                source.CopyTo(target);
                File.SetLastWriteTimeUtc(target, source.LastWriteTimeUtc);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{source.Name}: {ex.Message}");
            }
        }

        // Items deleted from the library move to the archive as well.
        foreach (string mirrored in Directory.GetFiles(CurrentFolder).Where(f => !present.Contains(Path.GetFileName(f))))
        {
            try
            {
                Directory.CreateDirectory(archive);
                File.Move(mirrored, Path.Combine(archive, Path.GetFileName(mirrored)));
                archived++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(mirrored)}: {ex.Message}");
            }
        }

        Prune(keepDays);
        return new TrayMirrorResult(copied, archived, errors);
    }

    private void Prune(int keepDays)
    {
        if (!Directory.Exists(OlderFolder))
            return;
        foreach (string dir in Directory.GetDirectories(OlderFolder).Where(d => Directory.GetCreationTime(d) < DateTime.Now.AddDays(-keepDays)))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next time */ }
        }
    }

    private static bool IsUnchanged(FileInfo source, string target)
    {
        var copy = new FileInfo(target);
        return copy.Exists && copy.Length == source.Length && copy.LastWriteTimeUtc == source.LastWriteTimeUtc;
    }
}
