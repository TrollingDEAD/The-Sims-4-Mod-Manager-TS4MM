using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.Core;

/// <summary>Outcome of applying a profile.</summary>
/// <param name="MissingModIds">Mods the profile wants enabled that no longer exist in the Mods folder.</param>
public sealed record ProfileApplyResult(IReadOnlyList<ToggleFailure> Failures, IReadOnlyList<string> MissingModIds);

/// <summary>
/// Persists and applies named mod profiles as one JSON file per profile under %AppData%.
/// The profile's display name is stored inside the file; the file name is only a sanitized,
/// collision-free derivative of it, so names with characters like '/' or ':' survive unchanged.
/// Corrupt files are skipped (or recovered from their backup) instead of crashing the app.
/// </summary>
public sealed class ProfileStore
{
    private const string Extension = ".json";

    private readonly string _profilesDirectory;

    public ProfileStore(string? profilesDirectory = null)
    {
        _profilesDirectory = profilesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sims4ModManager", "profiles");
    }

    /// <summary>All profiles, for bundling into a portable settings backup.</summary>
    public IReadOnlyList<ModProfile> LoadAllProfiles() => LoadAll().Select(e => e.Profile).ToList();

    public IReadOnlyList<string> ListProfileNames() =>
        LoadAll()
            .Select(p => p.Profile.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Loads a profile by name (case-insensitive); null if it does not exist or is unreadable.</summary>
    public ModProfile? Load(string name) => Find(name)?.Profile;

    /// <summary>
    /// Saves the profile, replacing an existing profile with the same name (case-insensitive).
    /// Throws <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/> if it cannot be written.
    /// </summary>
    public void Save(ModProfile profile)
    {
        profile.UpdatedUtc = DateTime.UtcNow;
        profile.SchemaVersion = ModProfile.CurrentSchemaVersion;

        string path = Find(profile.Name)?.Path ?? GetFreePath(profile.Name);
        JsonFile.WriteAtomic(path, profile);
    }

    public void Delete(string name)
    {
        var existing = Find(name);
        if (existing is not null)
            JsonFile.Delete(existing.Value.Path);
    }

    public static ModProfile CaptureCurrent(string name, IEnumerable<ModEntry> mods, string? modsPath = null) => new()
    {
        Name = name,
        ModsPath = modsPath,
        EnabledModIds = mods.Where(m => m.IsEnabled).Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>
    /// Toggles every mod to match the profile's enabled set. Per-file failures (e.g. a file locked by a
    /// running game) are collected instead of thrown, and mods the profile references but that are no
    /// longer installed are reported.
    /// </summary>
    public static ProfileApplyResult Apply(ModProfile profile, IReadOnlyList<ModEntry> allMods, Backup.ChangeRecorder? recorder = null)
    {
        var enabledIds = new HashSet<string>(profile.EnabledModIds, StringComparer.OrdinalIgnoreCase);
        var failures = new List<ToggleFailure>();

        foreach (var mod in allMods)
        {
            bool shouldBeEnabled = enabledIds.Contains(mod.Id);
            if (mod.IsEnabled == shouldBeEnabled)
                continue;

            var result = ModToggleService.SetEnabled(mod, shouldBeEnabled, recorder);
            if (!result.Success)
                failures.AddRange(result.Failures);
        }

        var installedIds = allMods.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = enabledIds
            .Where(id => !installedIds.Contains(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProfileApplyResult(failures, missing);
    }

    private IEnumerable<(string Path, ModProfile Profile)> LoadAll()
    {
        if (!Directory.Exists(_profilesDirectory))
            yield break;

        foreach (string path in Directory.EnumerateFiles(_profilesDirectory, "*" + Extension))
        {
            var profile = JsonFile.TryRead<ModProfile>(path);
            if (profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
                yield return (path, profile);
        }
    }

    private (string Path, ModProfile Profile)? Find(string name)
    {
        foreach (var entry in LoadAll())
        {
            if (string.Equals(entry.Profile.Name, name, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    /// <summary>Sanitized file name for a new profile, suffixed if another profile already uses it.</summary>
    private string GetFreePath(string name)
    {
        string baseName = SanitizeFileName(name);
        string path = Path.Combine(_profilesDirectory, baseName + Extension);
        int suffix = 2;
        while (File.Exists(path) || File.Exists(path + JsonFile.BackupSuffix))
        {
            path = Path.Combine(_profilesDirectory, $"{baseName} ({suffix}){Extension}");
            suffix++;
        }
        return path;
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0)
            return "Profil";
        return ReservedDeviceNames.Contains(name) ? "_" + name : name;
    }
}
