using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Saves;

public sealed record SaveBackup(string Folder, int Slot, string Name, DateTime CreatedLocal, long SizeBytes, int FileCount, bool IsAutomatic = false);

/// <summary>
/// Copies of save slots (main file + the game's automatic versions) in
/// %AppData%\Sims4ModManager\backups\saves. Restoring goes through the change journal, so the
/// overwritten current save is kept as well.
/// </summary>
public sealed class SaveBackups
{
    private const string AutomaticMarker = "Automatische Sicherung";

    public SaveBackups(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sims4ModManager", "backups", "saves");
    }

    public string Root { get; }

    /// <summary>
    /// Copies a save slot. Automatic backups skip the game's own older versions (they multiply the size)
    /// and are thinned out by <see cref="Prune"/>; manual ones are kept until the user deletes them.
    /// </summary>
    public SaveBackup Create(SaveGameInfo save, bool automatic = false)
    {
        string label = save.Slot < 0 ? L.T("Zwischenstand") : L.F("Slot {0}", save.Slot);
        string folder = Tray.TrayHousekeeping.UniquePath(Path.Combine(Root, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss} {label}"));
        Directory.CreateDirectory(folder);
        var files = automatic ? new[] { save.Path } : new[] { save.Path }.Concat(save.VersionFiles);
        foreach (string file in files)
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(folder, "info.txt"),
            $"{label}: {save.Name}\r\nGespeichert mit Spielversion {save.SavedWithVersion}\r\n" + (automatic ? AutomaticMarker + "\r\n" : ""));
        return Describe(folder)!;
    }

    public IReadOnlyList<SaveBackup> List()
    {
        if (!Directory.Exists(Root))
            return Array.Empty<SaveBackup>();
        return Directory.GetDirectories(Root).Select(Describe).OfType<SaveBackup>()
            .OrderByDescending(b => b.CreatedLocal).ToList();
    }

    /// <summary>Deletes automatic backups beyond the newest <paramref name="keepPerSlot"/> of each slot. Returns how many were deleted.</summary>
    public int Prune(int keepPerSlot)
    {
        int deleted = 0;
        foreach (var slot in List().Where(b => b.IsAutomatic).GroupBy(b => b.Slot))
        {
            foreach (var old in slot.OrderByDescending(b => b.CreatedLocal).Skip(Math.Max(1, keepPerSlot)))
            {
                try
                {
                    Directory.Delete(old.Folder, recursive: true);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // try again next time
                }
            }
        }
        return deleted;
    }

    /// <summary>Copies the backup back into the saves folder (the current files are journaled first).</summary>
    public int Restore(SaveBackup backup, string gameDataFolder, ChangeRecorder recorder)
    {
        string saves = Path.Combine(gameDataFolder, "saves");
        int count = 0;
        foreach (string file in Directory.GetFiles(backup.Folder, "Slot_*"))
        {
            recorder.CopyIn(file, Path.Combine(saves, Path.GetFileName(file)));
            count++;
        }
        return count;
    }

    private static SaveBackup? Describe(string folder)
    {
        var files = Directory.GetFiles(folder, "Slot_*");
        string? main = files.FirstOrDefault(f => f.EndsWith(".save", StringComparison.OrdinalIgnoreCase));
        if (main is null)
            return null;
        string slotHex = Path.GetFileNameWithoutExtension(main).Replace("Slot_", "", StringComparison.OrdinalIgnoreCase);
        int slot = int.TryParse(slotHex, System.Globalization.NumberStyles.HexNumber, null, out int s) ? s : 0;
        string infoPath = Path.Combine(folder, "info.txt");
        var info = File.Exists(infoPath) ? File.ReadAllLines(infoPath) : Array.Empty<string>();
        string first = info.FirstOrDefault() ?? "";
        string name = first.Contains(':') ? first[(first.IndexOf(':') + 1)..].Trim() : "";
        bool automatic = info.Contains(AutomaticMarker);
        return new SaveBackup(folder, slot, name, Directory.GetCreationTime(folder), files.Sum(f => new FileInfo(f).Length), files.Length, automatic);
    }
}
