using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core;

/// <summary>
/// Central place for the naming convention used to disable mods: appending ".disabled"
/// to the real extension (e.g. "MyMod.package" -&gt; "MyMod.package.disabled"). The game only
/// loads files it recognizes by extension, so this reversibly hides a file without moving it -
/// which keeps relative paths inside multi-file mods intact.
/// </summary>
public static class ModFileNaming
{
    public const string PackageExtension = ".package";
    public const string ScriptExtension = ".ts4script";
    public const string DisabledSuffix = ".disabled";

    public static bool IsDisabled(string fileName) =>
        fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Strips a trailing ".disabled" suffix, if present, to get at the real extension.</summary>
    public static string GetEffectiveFileName(string fileName) =>
        IsDisabled(fileName) ? fileName[..^DisabledSuffix.Length] : fileName;

    public static ModFileKind ClassifyKind(string fileName)
    {
        string effective = GetEffectiveFileName(fileName);
        if (effective.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
            return ModFileKind.Package;
        if (effective.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase))
            return ModFileKind.Script;
        return ModFileKind.Other;
    }

    public static bool IsManagedModFile(string fileName) => ClassifyKind(fileName) != ModFileKind.Other;

    public static string ToEnabledPath(string path) =>
        IsDisabled(path) ? path[..^DisabledSuffix.Length] : path;

    public static string ToDisabledPath(string path) =>
        IsDisabled(path) ? path : path + DisabledSuffix;
}
