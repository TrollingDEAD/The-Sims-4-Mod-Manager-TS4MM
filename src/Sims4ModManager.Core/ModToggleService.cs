using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core;

public sealed record ToggleFailure(string FilePath, string Message);

public sealed record ToggleResult(bool Success, IReadOnlyList<ToggleFailure> Failures)
{
    public static ToggleResult Ok { get; } = new(true, Array.Empty<ToggleFailure>());
}

/// <summary>
/// Enables/disables mods by renaming their files' extensions (see <see cref="ModFileNaming"/>).
/// Operates on every file that belongs to a <see cref="ModEntry"/> so multi-file mods are
/// switched atomically-ish: if one rename fails (e.g. the game has the file open), the others
/// are still attempted and every failure is reported back instead of throwing.
/// With a <see cref="ChangeRecorder"/> every rename is journaled and can be undone.
/// </summary>
public static class ModToggleService
{
    public static ToggleResult SetEnabled(ModEntry entry, bool enable, ChangeRecorder? recorder = null)
    {
        var failures = entry.Files
            .Select(file => SetFileEnabledCore(file, enable, recorder))
            .OfType<ToggleFailure>()
            .ToList();

        return failures.Count == 0 ? ToggleResult.Ok : new ToggleResult(false, failures);
    }

    /// <summary>Switches a single file of a mod (used by conflict resolution for multi-file mods).</summary>
    public static ToggleResult SetFileEnabled(ModFileInfo file, bool enable, ChangeRecorder? recorder = null)
    {
        var failure = SetFileEnabledCore(file, enable, recorder);
        return failure is null ? ToggleResult.Ok : new ToggleResult(false, new[] { failure });
    }

    private static ToggleFailure? SetFileEnabledCore(ModFileInfo file, bool enable, ChangeRecorder? recorder)
    {
        if (file.IsEnabled == enable)
            return null;

        string targetPath = enable
            ? ModFileNaming.ToEnabledPath(file.AbsolutePath)
            : ModFileNaming.ToDisabledPath(file.AbsolutePath);

        if (string.Equals(targetPath, file.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            if (File.Exists(targetPath))
                return new ToggleFailure(file.AbsolutePath, L.F("Zieldatei existiert bereits: {0}", Path.GetFileName(targetPath)));

            if (recorder is null)
                File.Move(file.AbsolutePath, targetPath);
            else
                recorder.Move(file.AbsolutePath, targetPath);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ToggleFailure(file.AbsolutePath, ex.Message);
        }
    }
}
