using System.Text.Json;

namespace Sims4ModManager.Core.Persistence;

/// <summary>
/// Crash-safe JSON persistence: writes go to a temp file that then atomically replaces the target,
/// keeping the previous version as "&lt;file&gt;.bak". Reads fall back to that backup if the main file
/// is missing or corrupt, so an interrupted write or a damaged file never loses the user's data.
/// </summary>
public static class JsonFile
{
    public const string BackupSuffix = ".bak";
    private const string TempSuffix = ".tmp";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Reads the file (or its backup). Returns null if neither exists or both are unreadable.</summary>
    public static T? TryRead<T>(string path) where T : class =>
        TryReadSingle<T>(path) ?? TryReadSingle<T>(path + BackupSuffix);

    /// <summary>Atomically writes <paramref name="value"/>. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> if the file cannot be written.</summary>
    public static void WriteAtomic<T>(string path, T value)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tempPath = path + TempSuffix;
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Options);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
            File.Replace(tempPath, path, path + BackupSuffix, ignoreMetadataErrors: true);
        else
            File.Move(tempPath, path);
    }

    /// <summary>Deletes the file together with its backup and any leftover temp file.</summary>
    public static void Delete(string path)
    {
        File.Delete(path);
        File.Delete(path + BackupSuffix);
        File.Delete(path + TempSuffix);
    }

    private static T? TryReadSingle<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
