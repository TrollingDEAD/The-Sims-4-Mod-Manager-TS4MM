using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.Core.Backup;

public sealed record SnapshotFile(string Path, long Size, long WriteTicks, bool Enabled);

/// <summary>State of the Mods folder at one point in time (taken daily and before every game start).</summary>
public sealed class ModSnapshot
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ModsPath { get; set; } = string.Empty;
    public List<SnapshotFile> Files { get; set; } = new();

    public static ModSnapshot Capture(string modsPath, IEnumerable<ModEntry> mods, string reason) => new()
    {
        Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),
        CreatedUtc = DateTime.UtcNow,
        Reason = reason,
        ModsPath = modsPath,
        Files = mods.SelectMany(m => m.Files)
            .Select(f => new SnapshotFile(
                System.IO.Path.GetRelativePath(modsPath, ModFileNaming.ToEnabledPath(f.AbsolutePath)),
                f.SizeBytes, f.LastWriteUtc.Ticks, f.IsEnabled))
            .ToList()
    };
}

public sealed record SnapshotMove(string From, string To);

/// <summary>What changed between two snapshots (paths relative to the Mods folder).</summary>
public sealed record SnapshotDiff(
    IReadOnlyList<SnapshotFile> Added,
    IReadOnlyList<SnapshotFile> Removed,
    IReadOnlyList<SnapshotFile> Changed,
    IReadOnlyList<SnapshotFile> Enabled,
    IReadOnlyList<SnapshotFile> Disabled,
    IReadOnlyList<SnapshotMove> Moved)
{
    public int Total => Added.Count + Removed.Count + Changed.Count + Enabled.Count + Disabled.Count + Moved.Count;
}

/// <summary>Snapshots in %AppData%\Sims4ModManager\snapshots for "Was hat sich geändert?".</summary>
public sealed class ModSnapshotStore
{
    public const int DefaultKeep = 30;
    public const string ReasonDaily = "Tagesstand";
    public const string ReasonPlay = "Spielstart";

    public ModSnapshotStore(string? root = null)
    {
        Root = root ?? Path.Combine(AppPaths.Root, "snapshots");
    }

    public string Root { get; }

    /// <summary>All snapshots of <paramref name="modsPath"/>, newest first.</summary>
    public IReadOnlyList<ModSnapshot> List(string modsPath)
    {
        if (!Directory.Exists(Root))
            return Array.Empty<ModSnapshot>();
        return Directory.GetFiles(Root, "*.json")
            .Select(JsonFile.TryRead<ModSnapshot>)
            .OfType<ModSnapshot>()
            .Where(s => string.Equals(s.ModsPath.TrimEnd('\\'), modsPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedUtc)
            .ToList();
    }

    public void Save(ModSnapshot snapshot, int keep = DefaultKeep)
    {
        JsonFile.WriteAtomic(Path.Combine(Root, snapshot.Id + ".json"), snapshot);
        foreach (string old in Directory.GetFiles(Root, "*.json").OrderByDescending(f => f).Skip(keep))
        {
            File.Delete(old);
            if (File.Exists(old + JsonFile.BackupSuffix))
                File.Delete(old + JsonFile.BackupSuffix);
        }
    }

    /// <summary>Takes the daily snapshot unless today's exists already. Returns true if one was taken.</summary>
    public bool EnsureDaily(string modsPath, IEnumerable<ModEntry> mods)
    {
        var latest = List(modsPath).FirstOrDefault();
        if (latest is not null && latest.CreatedUtc.ToLocalTime().Date == DateTime.Now.Date)
            return false;
        Save(ModSnapshot.Capture(modsPath, mods, ReasonDaily));
        return true;
    }

    public static SnapshotDiff Compare(ModSnapshot before, ModSnapshot after)
    {
        var old = before.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var now = after.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var added = now.Values.Where(f => !old.ContainsKey(f.Path)).ToList();
        var removed = old.Values.Where(f => !now.ContainsKey(f.Path)).ToList();
        var both = now.Values.Where(f => old.ContainsKey(f.Path)).Select(f => (Old: old[f.Path], New: f)).ToList();

        // Same name and size in another folder = moved (e.g. by sorting), not removed + added.
        var moved = new List<SnapshotMove>();
        foreach (var gone in removed.ToList())
        {
            var match = added.FirstOrDefault(a => a.Size == gone.Size
                && string.Equals(Path.GetFileName(a.Path), Path.GetFileName(gone.Path), StringComparison.OrdinalIgnoreCase));
            if (match is null)
                continue;
            moved.Add(new SnapshotMove(gone.Path, match.Path));
            removed.Remove(gone);
            added.Remove(match);
        }

        return new SnapshotDiff(
            added.OrderBy(f => f.Path).ToList(),
            removed.OrderBy(f => f.Path).ToList(),
            both.Where(x => x.Old.Size != x.New.Size || x.Old.WriteTicks != x.New.WriteTicks).Select(x => x.New).OrderBy(f => f.Path).ToList(),
            both.Where(x => !x.Old.Enabled && x.New.Enabled).Select(x => x.New).OrderBy(f => f.Path).ToList(),
            both.Where(x => x.Old.Enabled && !x.New.Enabled).Select(x => x.New).OrderBy(f => f.Path).ToList(),
            moved.OrderBy(m => m.To).ToList());
    }
}
