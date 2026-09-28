using System.IO.Compression;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Scripts;

/// <summary>
/// Reads the Python modules contained in a .ts4script file (a plain zip archive the game adds to
/// its module search path). Only the zip central directory is read - CRC32 and size from there
/// are enough to tell whether two archives ship the same module version.
/// </summary>
public static class ScriptArchiveReader
{
    /// <summary>Returns null if the file is not a readable zip archive.</summary>
    public static IReadOnlyList<ScriptModule>? TryReadModules(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var modules = new List<ScriptModule>();

            foreach (var entry in archive.Entries)
            {
                string? modulePath = TryGetModulePath(entry.FullName);
                if (modulePath is not null)
                    modules.Add(new ScriptModule(modulePath, entry.FullName, entry.Crc32, entry.Length));
            }

            return modules;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>"Folder\Mod.pyc" -> "folder/mod"; null for non-Python entries and directories.</summary>
    internal static string? TryGetModulePath(string entryName)
    {
        string normalized = entryName.Replace('\\', '/');

        string withoutExtension;
        if (normalized.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase))
            withoutExtension = normalized[..^4];
        else if (normalized.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            withoutExtension = normalized[..^3];
        else
            return null;

        return withoutExtension.Length == 0 ? null : withoutExtension.ToLowerInvariant();
    }
}
