namespace Sims4ModManager.Core.Models;

/// <summary>
/// A single .package / .ts4script file that belongs to a <see cref="ModEntry"/>.
/// </summary>
public sealed class ModFileInfo
{
    public required string AbsolutePath { get; init; }
    public required string RelativePathInMod { get; init; }
    public required ModFileKind Kind { get; init; }
    public required bool IsEnabled { get; init; }
    public required long SizeBytes { get; init; }
    public DateTime LastWriteUtc { get; init; }

    /// <summary>
    /// Live (non-deleted) entries of this package's DBPF index (also read for disabled files). Empty for script files
    /// or when the package could not be parsed.
    /// </summary>
    public IReadOnlyList<PackageResource> Resources { get; init; } = Array.Empty<PackageResource>();

    /// <summary>Python modules inside this .ts4script archive. Empty for packages or unreadable archives.</summary>
    public IReadOnlyList<ScriptModule> ScriptModules { get; init; } = Array.Empty<ScriptModule>();

    /// <summary>
    /// True when this file is enabled but its contents (DBPF index or zip directory) could not be read.
    /// Such files are excluded from conflict detection and reported separately instead of being
    /// silently treated as conflict-free.
    /// </summary>
    public bool IsUnreadable { get; init; }
}
