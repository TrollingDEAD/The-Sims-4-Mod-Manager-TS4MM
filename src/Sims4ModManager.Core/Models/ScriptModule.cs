namespace Sims4ModManager.Core.Models;

/// <summary>
/// A Python module inside a .ts4script archive. <see cref="ModulePath"/> is normalized
/// (lower-case, forward slashes, no .py/.pyc extension) so the same module is recognized
/// regardless of whether it ships as source or bytecode.
/// </summary>
public readonly record struct ScriptModule(string ModulePath, string EntryName, uint Crc32, long Length)
{
    /// <summary>Content fingerprint taken from the zip central directory - no decompression needed.</summary>
    public string Fingerprint => $"{Crc32:X8}-{Length}";
}
