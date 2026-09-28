namespace Sims4ModManager.Core.Models;

/// <summary>A named snapshot of which mods (by <see cref="ModEntry.Id"/>) should be enabled.</summary>
public sealed class ModProfile
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public required string Name { get; set; }
    public required HashSet<string> EnabledModIds { get; set; }

    /// <summary>Mods folder the profile was captured from (null for profiles saved by older versions).</summary>
    public string? ModsPath { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
