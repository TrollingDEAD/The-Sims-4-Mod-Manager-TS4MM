using Sims4ModManager.Core.Persistence;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;

    /// <summary>Defaults to 1 so files written before versioning existed are recognized as such; Load() upgrades it.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Mods folder explicitly chosen by the user; null means "use auto-detection".</summary>
    public string? ModsPath { get; set; }

    /// <summary>Recently used Mods folders, most recent first.</summary>
    public List<string> RecentModsPaths { get; set; } = new();

    public string? LastProfileName { get; set; }

    public WindowPlacement? Window { get; set; }

    /// <summary>Game version seen at the last health check - used to detect game updates.</summary>
    public string? LastSeenGameVersion { get; set; }

    /// <summary>Script mods the user marked as trusted in the safety check ("file name|size").</summary>
    public List<string> TrustedScripts { get; set; } = new();

    /// <summary>Watch the Windows Downloads folder for new Sims 4 downloads.</summary>
    public bool WatchDownloads { get; set; } = true;

    /// <summary>Options of the "Spielen" button.</summary>
    public bool ClearCacheBeforePlay { get; set; } = true;
    public bool BackupSavesBeforePlay { get; set; }
    public string? PlayProfileName { get; set; }

    /// <summary>UI theme: "Dark" (default) or "Light".</summary>
    public string? Theme { get; set; }

    /// <summary>UI language: "de" (default) or "en". Takes effect after a restart.</summary>
    public string? Language { get; set; }

    /// <summary>Also mirror the Tray folder before playing (incremental, see TrayMirror).</summary>
    public bool BackupTrayBeforePlay { get; set; }

    /// <summary>Automatic backup of saves (and Tray, if enabled) at app start every n days; 0 = off.</summary>
    public int AutoBackupIntervalDays { get; set; }

    /// <summary>Automatic save backups kept per slot.</summary>
    public int KeepAutoBackupsPerSlot { get; set; } = 5;

    public DateTime? LastAutoBackupUtc { get; set; }

    /// <summary>The setup assistant ran (or was dismissed).</summary>
    public bool SetupCompleted { get; set; }

    /// <summary>"Sortieren": one subfolder per creator inside each category folder.</summary>
    public bool SortByCreator { get; set; } = true;

    /// <summary>Game installation chosen by the user; null means "detect" (registry, EA app, Steam).</summary>
    public string? GameInstallPath { get; set; }

    /// <summary>CurseForge API key, encrypted for the current Windows user (DPAPI, base64). Never stored in plain text.</summary>
    public string? CurseForgeApiKeyProtected { get; set; }

    public DateTime? LastUpdateCheckUtc { get; set; }
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }
}

public enum ModsPathSource
{
    Saved,
    AutoDetected,
    None
}

/// <summary>Which Mods folder to use at startup, where it came from, and anything the user should know.</summary>
public sealed record ModsPathResolution(string? Path, ModsPathSource Source, string? Notice);

/// <summary>Persists app-level configuration (Mods folder, recent folders, last profile, window placement).</summary>
public sealed class AppSettingsStore
{
    public const int MaxRecentModsPaths = 8;

    private readonly string _filePath;
    private readonly Func<string?> _autoDetect;

    public AppSettingsStore(string? filePath = null, Func<string?>? autoDetect = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sims4ModManager", "settings.json");
        _autoDetect = autoDetect ?? (() => ModsFolderLocator.TryAutoDetect());
    }

    /// <summary>Loads settings; missing or corrupt files (with no usable backup) yield defaults.</summary>
    public AppSettings Load()
    {
        var settings = JsonFile.TryRead<AppSettings>(_filePath) ?? new AppSettings();
        settings.RecentModsPaths ??= new List<string>();

        // Settings from before the recent list existed: seed it with the saved folder.
        if (settings.SchemaVersion < 2 && !string.IsNullOrWhiteSpace(settings.ModsPath) && settings.RecentModsPaths.Count == 0)
            settings.RecentModsPaths.Add(settings.ModsPath);

        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return settings;
    }

    /// <summary>Saves settings; returns false instead of throwing if the file cannot be written.</summary>
    public bool TrySave(AppSettings settings)
    {
        try
        {
            JsonFile.WriteAtomic(_filePath, settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Loads, modifies and saves in one step so unrelated settings are never overwritten with stale values.</summary>
    public bool TryUpdate(Action<AppSettings> update)
    {
        var settings = Load();
        update(settings);
        return TrySave(settings);
    }

    /// <summary>Stores <paramref name="modsPath"/> as the chosen folder and moves it to the top of the recent list.</summary>
    public bool TryRememberModsPath(string modsPath) => TryUpdate(settings =>
    {
        settings.ModsPath = modsPath;
        AddRecent(settings, modsPath);
    });

    public static void AddRecent(AppSettings settings, string modsPath)
    {
        settings.RecentModsPaths.RemoveAll(p => string.Equals(p, modsPath, StringComparison.OrdinalIgnoreCase));
        settings.RecentModsPaths.Insert(0, modsPath);
        if (settings.RecentModsPaths.Count > MaxRecentModsPaths)
            settings.RecentModsPaths.RemoveRange(MaxRecentModsPaths, settings.RecentModsPaths.Count - MaxRecentModsPaths);
    }

    /// <summary>
    /// Resolves the Mods folder to use: the saved choice if it still exists, otherwise auto-detection.
    /// A saved folder that has disappeared (renamed, drive missing) is reported instead of silently replaced.
    /// </summary>
    public ModsPathResolution ResolveModsPath()
    {
        var settings = Load();
        string? saved = settings.ModsPath;

        if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
            return new ModsPathResolution(saved, ModsPathSource.Saved, null);

        string? detected = _autoDetect();
        string? missingNotice = string.IsNullOrWhiteSpace(saved)
            ? null
            : L.F("Gespeicherter Mods-Ordner nicht gefunden: {0}.", saved);

        if (detected is not null)
        {
            string notice = missingNotice is null
                ? L.F("Mods-Ordner automatisch erkannt: {0}", detected)
                : L.F("{0} Stattdessen automatisch erkannt: {1}", missingNotice, detected);
            return new ModsPathResolution(detected, ModsPathSource.AutoDetected, notice);
        }

        return new ModsPathResolution(null, ModsPathSource.None,
            (missingNotice is null ? "" : missingNotice + " ") + L.T("Kein Mods-Ordner gefunden. Bitte manuell auswählen."));
    }
}
