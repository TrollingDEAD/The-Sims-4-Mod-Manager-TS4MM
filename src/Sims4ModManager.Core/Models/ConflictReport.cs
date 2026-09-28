namespace Sims4ModManager.Core.Models;

public enum ConflictSeverity
{
    Low,
    Medium,
    High
}

public enum ConflictItemKind
{
    Resource,
    ScriptModule
}

/// <summary>A mod file that provides a conflicting resource or script module.</summary>
public sealed record ConflictSource(ModEntry Mod, ModFileInfo File);

/// <summary>One resource key or script module that differing versions of several mods provide.</summary>
public sealed class ConflictItem
{
    /// <summary>The files providing this item (at least two, from different mods).</summary>
    public IReadOnlyList<ConflictSource> Sources { get; init; } = Array.Empty<ConflictSource>();

    public required ConflictItemKind Kind { get; init; }
    public ResourceKey? Key { get; init; }
    public string? ModulePath { get; init; }
    public required string TypeName { get; init; }
    public required ConflictSeverity Severity { get; init; }

    public string Label => Kind == ConflictItemKind.Resource ? Key.ToString()! : ModulePath!;
}

/// <summary>
/// All conflict items shared by exactly the same set of mods. Grouping by mod set keeps the list
/// readable: two CC packs overlapping in 300 resources show up as one entry, not 300.
/// </summary>
public sealed class ConflictGroup
{
    public required IReadOnlyList<ModEntry> Mods { get; init; }
    public required IReadOnlyList<ConflictItem> Items { get; init; }

    public ConflictSeverity Severity => Items.Max(i => i.Severity);
}

/// <summary>An enabled mod file whose contents could not be inspected (corrupt, locked, unsupported).</summary>
public sealed record UnreadableModFile(ModEntry Mod, ModFileInfo File);

public sealed class ConflictReport
{
    public static readonly ConflictReport Empty = new()
    {
        Groups = Array.Empty<ConflictGroup>(),
        UnreadableFiles = Array.Empty<UnreadableModFile>(),
        IgnoredIdenticalCount = 0
    };

    public required IReadOnlyList<ConflictGroup> Groups { get; init; }
    public required IReadOnlyList<UnreadableModFile> UnreadableFiles { get; init; }

    /// <summary>Keys/modules shared by several mods but byte-identical everywhere, hence harmless.</summary>
    public required int IgnoredIdenticalCount { get; init; }

    public int TotalItemCount => Groups.Sum(g => g.Items.Count);
}
