namespace Sims4ModManager.Core;

/// <summary>
/// A Sims 4 user-data folder (e.g. "Documents\Electronic Arts\Die Sims 4") found on disk.
/// </summary>
/// <param name="MarkerCount">How many typical game files/folders exist (Options.ini, saves, ...) - 0 means "name matches only".</param>
/// <param name="LastActivityUtc">Most recent write time of files the game touches while running.</param>
/// <param name="MisplacedModFiles">.package/.ts4script files lying directly in the data folder, where the game ignores them.</param>
public sealed record GameDataFolder(
    string Path,
    string ModsPath,
    bool ModsFolderExists,
    int MarkerCount,
    DateTime LastActivityUtc,
    IReadOnlyList<string> MisplacedModFiles);

/// <summary>
/// Locates the Sims 4 Mods folder. The user-data folder name depends on the game language
/// ("The Sims 4", "Die Sims 4", "Les Sims 4", ...) and Documents may be redirected (OneDrive,
/// custom library location), so instead of assuming one fixed path this scans every plausible
/// Documents root for folders that look like Sims 4 user data and ranks them.
/// </summary>
public static class ModsFolderLocator
{
    public const string ModsFolderName = "Mods";
    private const string PublisherFolderName = "Electronic Arts";

    /// <summary>Files/folders the game creates in its user-data folder.</summary>
    private static readonly string[] MarkerNames =
    {
        "Options.ini", "GameVersion.txt", "UserSetting.ini", "Config.log", "saves", "Tray"
    };

    /// <summary>Candidate Documents folders, most likely first. Only existing folders are returned.</summary>
    public static IReadOnlyList<string> GetDocumentsRoots()
    {
        var roots = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var oneDriveRoots = new[]
        {
            Environment.GetEnvironmentVariable("OneDrive"),
            Environment.GetEnvironmentVariable("OneDriveConsumer"),
            Environment.GetEnvironmentVariable("OneDriveCommercial"),
            string.IsNullOrEmpty(userProfile) ? null : Path.Combine(userProfile, "OneDrive"),
        };

        if (!string.IsNullOrEmpty(userProfile))
            roots.Add(Path.Combine(userProfile, "Documents"));

        // OneDrive may show the Documents folder under its localized name.
        foreach (string? oneDrive in oneDriveRoots.Where(r => !string.IsNullOrEmpty(r)))
        {
            roots.Add(Path.Combine(oneDrive!, "Documents"));
            roots.Add(Path.Combine(oneDrive!, "Dokumente"));
        }

        return roots
            .Where(r => !string.IsNullOrEmpty(r))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>
    /// Finds all Sims 4 user-data folders below "Electronic Arts" in the given (or default) Documents
    /// roots, best candidate first: folders with an existing Mods folder, then real game data
    /// (marker files), then most recently used.
    /// </summary>
    public static IReadOnlyList<GameDataFolder> FindGameDataFolders(IEnumerable<string>? documentsRoots = null)
    {
        var found = new List<GameDataFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in documentsRoots ?? GetDocumentsRoots())
        {
            string publisherDir = Path.Combine(root, PublisherFolderName);
            foreach (string dir in SafeEnumerateDirectories(publisherDir))
            {
                if (!seen.Add(NormalizePath(dir)))
                    continue;

                var candidate = Inspect(dir);
                bool nameMatches = Path.GetFileName(dir).Contains("Sims 4", StringComparison.OrdinalIgnoreCase);
                if (nameMatches || candidate.MarkerCount >= 2)
                    found.Add(candidate);
            }
        }

        return found
            .OrderByDescending(c => c.ModsFolderExists)
            .ThenByDescending(c => c.MarkerCount > 0)
            .ThenByDescending(c => c.LastActivityUtc)
            .ToList();
    }

    /// <summary>Returns the Mods folder of the best candidate that has one, or null.</summary>
    public static string? TryAutoDetect(IEnumerable<string>? documentsRoots = null) =>
        FindGameDataFolders(documentsRoots).FirstOrDefault(c => c.ModsFolderExists)?.ModsPath;

    /// <summary>
    /// Corrects a common mistake when picking the folder manually: selecting the game's data folder
    /// ("...\Die Sims 4") instead of its Mods subfolder. Other paths are returned unchanged (normalized).
    /// </summary>
    public static string NormalizeSelectedFolder(string path)
    {
        string normalized = NormalizePath(path);
        string modsSubfolder = Path.Combine(normalized, ModsFolderName);

        bool isDataFolder = !string.Equals(Path.GetFileName(normalized), ModsFolderName, StringComparison.OrdinalIgnoreCase)
                            && Directory.Exists(modsSubfolder)
                            && CountMarkers(normalized) >= 2;

        return isDataFolder ? modsSubfolder : normalized;
    }

    /// <summary>
    /// If <paramref name="modsPath"/> is the Mods folder of a game data folder, returns that folder's
    /// details (e.g. to warn about misplaced mod files); otherwise null.
    /// </summary>
    public static GameDataFolder? TryGetGameDataFolder(string modsPath)
    {
        string? parent = Path.GetDirectoryName(NormalizePath(modsPath));
        if (parent is null || !Directory.Exists(parent))
            return null;

        var candidate = Inspect(parent);
        return candidate.MarkerCount > 0 ? candidate : null;
    }

    public static string NormalizePath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    private static GameDataFolder Inspect(string dir)
    {
        string modsPath = Path.Combine(dir, ModsFolderName);

        var activityTimes = MarkerNames
            .Append(ModsFolderName)
            .Select(name => LastWriteTimeUtcOrMin(Path.Combine(dir, name)));

        var misplaced = SafeEnumerateFiles(dir)
            .Where(f => ModFileNaming.ClassifyKind(Path.GetFileName(f)) != Models.ModFileKind.Other
                        && !ModFileNaming.IsDisabled(Path.GetFileName(f))
                        && !IsGameCacheFile(Path.GetFileName(f)))
            .ToList();

        return new GameDataFolder(
            NormalizePath(dir),
            NormalizePath(modsPath),
            Directory.Exists(modsPath),
            CountMarkers(dir),
            activityTimes.Max(),
            misplaced);
    }

    /// <summary>Package files the game itself keeps in the data folder (caches, databases).</summary>
    private static bool IsGameCacheFile(string fileName) =>
        fileName.EndsWith("cache.package", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith("DB.package", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("houseDescription", StringComparison.OrdinalIgnoreCase);

    private static DateTime LastWriteTimeUtcOrMin(string path)
    {
        if (File.Exists(path))
            return File.GetLastWriteTimeUtc(path);
        if (Directory.Exists(path))
            return Directory.GetLastWriteTimeUtc(path);
        return DateTime.MinValue;
    }

    private static int CountMarkers(string dir) =>
        MarkerNames.Count(name => File.Exists(Path.Combine(dir, name)) || Directory.Exists(Path.Combine(dir, name)));

    private static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir)
    {
        try
        {
            return Directory.GetFiles(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
