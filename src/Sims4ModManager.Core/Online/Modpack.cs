using Sims4ModManager.Core.Persistence;

namespace Sims4ModManager.Core.Online;

/// <summary>One mod referenced by CurseForge project/file ID, not by its actual files.</summary>
public sealed record ModpackEntry(long ModId, long FileId, string DisplayName);

/// <summary>
/// A shareable list of mods by CurseForge reference rather than the files themselves - a creator or
/// Discord/forum community can publish this small file; installing one resolves and downloads every
/// mod (and its dependencies) through the same CurseForge pipeline a manual Browse install uses.
/// Mirrors what Nexus/Vortex "Collections" and Mod Organizer 2's profile exports do for other games.
/// </summary>
public sealed class ModpackManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public DateTime ExportedUtc { get; init; } = DateTime.UtcNow;
    public string? AppVersion { get; init; }
    public string? Title { get; init; }
    public required IReadOnlyList<ModpackEntry> Mods { get; init; }
}

public static class ModpackFile
{
    public static void SaveToFile(ModpackManifest manifest, string path) => JsonFile.WriteAtomic(path, manifest);

    /// <summary>Null if the file doesn't exist, isn't valid JSON, is empty, or is a newer format version this build doesn't know.</summary>
    public static ModpackManifest? TryLoadFromFile(string path)
    {
        var manifest = JsonFile.TryRead<ModpackManifest>(path);
        return manifest is { FormatVersion: <= ModpackManifest.CurrentFormatVersion } && manifest.Mods.Count > 0 ? manifest : null;
    }
}
