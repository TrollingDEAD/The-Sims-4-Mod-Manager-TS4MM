namespace Sims4ModManager.Core.Models;

/// <summary>
/// One manageable unit in the Mods folder: either a single loose .package/.ts4script file, or a
/// folder containing one or more such files. Enable/disable always acts on the whole entry so
/// multi-file mods stay consistent.
/// </summary>
public sealed class ModEntry
{
    /// <summary>Stable identifier (lower-case file or folder name) used in profiles, notes and snapshots.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }
    public required string AbsolutePath { get; init; }
    public required bool IsFolder { get; init; }
    public required IReadOnlyList<ModFileInfo> Files { get; init; }
    public required DateTime LastModifiedUtc { get; init; }

    /// <summary>Collection folder the mod lives in, relative to the Mods folder ("" = directly in Mods).</summary>
    public string Collection { get; init; } = string.Empty;

    public bool IsEnabled => Files.Count > 0 && Files.All(f => f.IsEnabled);
    public bool IsPartiallyEnabled => Files.Any(f => f.IsEnabled) && !IsEnabled;
    public long TotalSizeBytes => Files.Sum(f => f.SizeBytes);
    public bool ContainsScript => Files.Any(f => f.Kind == ModFileKind.Script);
    public bool ContainsPackage => Files.Any(f => f.Kind == ModFileKind.Package);

    internal ModEntry WithId(string id) => new()
    {
        Id = id,
        DisplayName = DisplayName,
        AbsolutePath = AbsolutePath,
        IsFolder = IsFolder,
        Files = Files,
        LastModifiedUtc = LastModifiedUtc,
        Collection = Collection
    };
}
