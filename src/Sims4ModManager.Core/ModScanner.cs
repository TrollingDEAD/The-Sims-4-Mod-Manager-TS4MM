using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Scripts;

namespace Sims4ModManager.Core;

public static class ModScanner
{
    /// <summary>
    /// Marker file of a collection folder (e.g. "Mods\CAS - Haare" created by sorting): its loose files
    /// and subfolders are mods of their own instead of the folder being one mod. The game ignores it.
    /// </summary>
    public const string CollectionMarker = ".s4mm-sammlung";

    public static bool IsCollection(string directory) => File.Exists(Path.Combine(directory, CollectionMarker));

    /// <summary>
    /// Scans the Mods folder and groups its contents into manageable units: each loose file is its own
    /// mod, each folder (recursively) is one mod. Collection folders (<see cref="CollectionMarker"/>) are
    /// descended into, so their contents are grouped the same way. Folders containing no recognizable mod
    /// files are ignored.
    /// </summary>
    public static IReadOnlyList<ModEntry> Scan(string modsRootPath)
    {
        if (!Directory.Exists(modsRootPath))
            return Array.Empty<ModEntry>();

        var results = new List<ModEntry>();
        ScanFolder(modsRootPath, modsRootPath, results, depth: 0);

        // IDs are the bare names, so moving a mod into a collection keeps profiles working; the rare
        // clash between collections is resolved with the collection path.
        var unique = new HashSet<string>();
        for (int i = 0; i < results.Count; i++)
        {
            var mod = results[i];
            if (unique.Add(mod.Id))
                continue;
            string id = NormalizeId(mod.Collection + "/" + mod.Id);
            unique.Add(id);
            results[i] = mod.WithId(id);
        }

        return results.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ScanFolder(string modsRootPath, string folder, List<ModEntry> results, int depth)
    {
        string collection = depth == 0 ? string.Empty : Path.GetRelativePath(modsRootPath, folder);

        foreach (string filePath in SafeFiles(folder, SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(filePath);
            if (!ModFileNaming.IsManagedModFile(fileName))
                continue;

            var fileInfo = BuildFileInfo(filePath, fileName);
            results.Add(new ModEntry
            {
                // The ID must not change when the mod is toggled, or profiles could never re-enable it.
                Id = NormalizeId(ModFileNaming.GetEffectiveFileName(fileName)),
                DisplayName = StripKnownExtensions(ModFileNaming.GetEffectiveFileName(fileName)),
                AbsolutePath = filePath,
                IsFolder = false,
                Files = new[] { fileInfo },
                LastModifiedUtc = fileInfo.LastWriteUtc,
                Collection = collection
            });
        }

        foreach (string dirPath in SafeDirectories(folder))
        {
            if (depth < 8 && IsCollection(dirPath))
            {
                ScanFolder(modsRootPath, dirPath, results, depth + 1);
                continue;
            }

            var files = new List<ModFileInfo>();
            foreach (string filePath in SafeFiles(dirPath, SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (!ModFileNaming.IsManagedModFile(fileName))
                    continue;

                files.Add(BuildFileInfo(filePath, fileName, relativeTo: dirPath));
            }

            if (files.Count == 0)
                continue;

            results.Add(new ModEntry
            {
                Id = NormalizeId(Path.GetFileName(dirPath)),
                DisplayName = Path.GetFileName(dirPath),
                AbsolutePath = dirPath,
                IsFolder = true,
                Files = files,
                LastModifiedUtc = files.Max(f => f.LastWriteUtc),
                Collection = collection
            });
        }
    }

    private static string[] SafeFiles(string dir, SearchOption option)
    {
        try { return Directory.GetFiles(dir, "*", option); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static string[] SafeDirectories(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static ModFileInfo BuildFileInfo(string filePath, string fileName, string? relativeTo = null)
    {
        var kind = ModFileNaming.ClassifyKind(fileName);
        bool isEnabled = !ModFileNaming.IsDisabled(fileName);
        var info = new FileInfo(filePath);

        // Package indexes are read for disabled files too, so tray items can point to CC that only
        // needs re-enabling; conflict detection itself only looks at enabled files.
        IReadOnlyList<PackageResource>? resources = null;
        IReadOnlyList<ScriptModule>? modules = null;
        bool unreadable = false;
        if (kind == ModFileKind.Package)
        {
            resources = DbpfReader.TryReadIndex(filePath);
            unreadable = isEnabled && resources is null;
        }
        else if (kind == ModFileKind.Script)
        {
            modules = ScriptArchiveReader.TryReadModules(filePath);
            unreadable = isEnabled && modules is null;
        }

        string relativePath = relativeTo is null
            ? fileName
            : Path.GetRelativePath(relativeTo, filePath);

        return new ModFileInfo
        {
            AbsolutePath = filePath,
            RelativePathInMod = relativePath,
            Kind = kind,
            IsEnabled = isEnabled,
            SizeBytes = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            Resources = resources ?? Array.Empty<PackageResource>(),
            ScriptModules = modules ?? Array.Empty<ScriptModule>(),
            IsUnreadable = unreadable
        };
    }

    private static string StripKnownExtensions(string fileName)
    {
        if (fileName.EndsWith(ModFileNaming.PackageExtension, StringComparison.OrdinalIgnoreCase))
            return fileName[..^ModFileNaming.PackageExtension.Length];
        if (fileName.EndsWith(ModFileNaming.ScriptExtension, StringComparison.OrdinalIgnoreCase))
            return fileName[..^ModFileNaming.ScriptExtension.Length];
        return fileName;
    }

    private static string NormalizeId(string name) => name.Replace('\\', '/').ToLowerInvariant();
}
