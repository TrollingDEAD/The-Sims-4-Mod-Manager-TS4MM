using Sims4ModManager.Core.Backup;

namespace Sims4ModManager.Core.Health;

public enum HealthSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>Result of an automatic fix.</summary>
public sealed record HealthFixResult(int Fixed, IReadOnlyList<string> Errors)
{
    public static HealthFixResult From(int count, List<string> errors) => new(count, errors);
}

/// <summary>
/// One finding of the health check (e.g. "3 script mods are nested too deep") with the affected
/// paths and - if possible - an automatic fix that runs through the change journal.
/// </summary>
public sealed class HealthIssue
{
    /// <summary>Stable identifier of the check (e.g. "script-too-deep").</summary>
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required HealthSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

    /// <summary>Button text of the fix, null if the issue has to be solved by hand.</summary>
    public string? FixLabel { get; init; }

    /// <summary>The fix. Runs with a journal recorder so it can be undone.</summary>
    public Func<ChangeRecorder, HealthFixResult>? Fix { get; init; }

    /// <summary>Fixes that change nothing the game needs (moving clutter, removing empty folders, enabling switches) can run in bulk.</summary>
    public bool IsSafeToFixInBulk { get; init; }

    public bool CanFix => Fix is not null;
}
