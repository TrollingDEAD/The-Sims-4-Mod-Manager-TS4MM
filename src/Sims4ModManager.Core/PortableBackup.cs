using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.Core;

/// <summary>
/// Settings worth carrying to a new PC. Deliberately excludes anything machine-specific: the Mods
/// folder and its recent-list (the new machine has its own, and auto-detection already handles a
/// missing one), window placement, the last-seen game version/install path, and the CurseForge API
/// key (DPAPI-encrypted for the current Windows user - it cannot be decrypted anywhere else).
/// </summary>
public sealed class PortableSettings
{
    public List<string> TrustedScripts { get; set; } = new();
    public bool WatchDownloads { get; set; } = true;
    public bool ClearCacheBeforePlay { get; set; } = true;
    public bool BackupSavesBeforePlay { get; set; }
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public bool BackupTrayBeforePlay { get; set; }
    public int AutoBackupIntervalDays { get; set; }
    public int KeepAutoBackupsPerSlot { get; set; } = 5;
    public bool SortByCreator { get; set; } = true;
    public bool IncludePrereleaseUpdates { get; set; }
}

/// <summary>
/// A single-file bundle of everything a user would otherwise have to redo by hand on a new PC:
/// settings, mod notes/tags/favorites, and saved profiles. Not a backup of the mods themselves.
/// </summary>
public sealed class PortableBackup
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public DateTime ExportedUtc { get; set; }
    public string? AppVersion { get; set; }
    public PortableSettings Settings { get; set; } = new();
    public Dictionary<string, ModNote> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ModProfile> Profiles { get; set; } = new();
}

/// <summary>Builds, applies, and reads/writes a <see cref="PortableBackup"/>.</summary>
public static class PortableBackupService
{
    public static PortableBackup Capture(AppSettingsStore settingsStore, ModNotesStore notes, ProfileStore profiles)
    {
        var s = settingsStore.Load();
        return new PortableBackup
        {
            ExportedUtc = DateTime.UtcNow,
            AppVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
            Settings = new PortableSettings
            {
                TrustedScripts = new List<string>(s.TrustedScripts),
                WatchDownloads = s.WatchDownloads,
                ClearCacheBeforePlay = s.ClearCacheBeforePlay,
                BackupSavesBeforePlay = s.BackupSavesBeforePlay,
                Theme = s.Theme,
                Language = s.Language,
                BackupTrayBeforePlay = s.BackupTrayBeforePlay,
                AutoBackupIntervalDays = s.AutoBackupIntervalDays,
                KeepAutoBackupsPerSlot = s.KeepAutoBackupsPerSlot,
                SortByCreator = s.SortByCreator,
                IncludePrereleaseUpdates = s.IncludePrereleaseUpdates
            },
            Notes = new Dictionary<string, ModNote>(notes.AllNotes, StringComparer.OrdinalIgnoreCase),
            Profiles = profiles.LoadAllProfiles().ToList()
        };
    }

    /// <summary>
    /// Applies a backup: settings are merged into the current ones (fields not listed above are left
    /// untouched), notes/tags/favorites are merged per mod ID (the backup wins on conflicts), and
    /// profiles are added, overwriting an existing profile of the same name.
    /// </summary>
    public static void Apply(PortableBackup backup, AppSettingsStore settingsStore, ModNotesStore notes, ProfileStore profiles)
    {
        var p = backup.Settings;
        settingsStore.TryUpdate(s =>
        {
            s.TrustedScripts = new List<string>(p.TrustedScripts);
            s.WatchDownloads = p.WatchDownloads;
            s.ClearCacheBeforePlay = p.ClearCacheBeforePlay;
            s.BackupSavesBeforePlay = p.BackupSavesBeforePlay;
            s.Theme = p.Theme;
            s.Language = p.Language;
            s.BackupTrayBeforePlay = p.BackupTrayBeforePlay;
            s.AutoBackupIntervalDays = p.AutoBackupIntervalDays;
            s.KeepAutoBackupsPerSlot = p.KeepAutoBackupsPerSlot;
            s.SortByCreator = p.SortByCreator;
            s.IncludePrereleaseUpdates = p.IncludePrereleaseUpdates;
        });

        foreach (var (modId, note) in backup.Notes)
            notes.Set(modId, note);

        foreach (var profile in backup.Profiles)
            profiles.Save(profile);
    }

    public static void SaveToFile(PortableBackup backup, string path) => JsonFile.WriteAtomic(path, backup);

    public static PortableBackup? TryLoadFromFile(string path) => JsonFile.TryRead<PortableBackup>(path);
}
