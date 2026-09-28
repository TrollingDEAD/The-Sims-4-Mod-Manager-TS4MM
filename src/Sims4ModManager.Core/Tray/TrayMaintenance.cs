using System.IO.Compression;

namespace Sims4ModManager.Core.Tray;

/// <summary>Export, backup-protected deletion and folder backups for the library.</summary>
public static class TrayMaintenance
{
    /// <summary>
    /// Exports an item together with the CC it uses in the layout a download is expected to have:
    /// "&lt;name&gt;\Tray\..." (unchanged file names) and "&lt;name&gt;\Mods\&lt;mod&gt;\..." (disabled
    /// files are exported enabled). With <paramref name="asZip"/> a single .zip is written instead.
    /// Returns the created folder or zip path.
    /// </summary>
    public static string Export(TrayItem item, IEnumerable<TrayCcReference> cc, string targetDirectory, bool asZip)
    {
        string baseName = TrayInstaller.SanitizeFolderName(item.DisplayName);
        var files = new List<(string Source, string RelativeTarget)>();

        foreach (string file in item.Files)
            files.Add((file, Path.Combine("Tray", Path.GetFileName(file))));

        foreach (var reference in cc.DistinctBy(r => r.File.AbsolutePath))
        {
            string effectiveRelative = ModFileNaming.GetEffectiveFileName(reference.File.RelativePathInMod);
            string relative = reference.Mod.IsFolder
                ? Path.Combine("Mods", TrayInstaller.SanitizeFolderName(reference.Mod.DisplayName), effectiveRelative)
                : Path.Combine("Mods", effectiveRelative);
            files.Add((reference.File.AbsolutePath, relative));
        }

        if (asZip)
        {
            string zipPath = TrayHousekeeping.UniquePath(Path.Combine(targetDirectory, baseName + ".zip"));
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var (source, relative) in files)
                zip.CreateEntryFromFile(source, relative.Replace('\\', '/'), CompressionLevel.Optimal);
            return zipPath;
        }

        string folder = TrayHousekeeping.UniquePath(Path.Combine(targetDirectory, baseName));
        foreach (var (source, relative) in files)
        {
            string destination = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
        return folder;
    }

    /// <summary>
    /// Removes an item from the library by moving its files into the change set of <paramref name="recorder"/>
    /// (never a hard delete) - undoing the change set restores it. Returns the backup folder.
    /// </summary>
    public static string DeleteToBackup(TrayItem item, Backup.ChangeRecorder recorder)
    {
        foreach (string file in item.Files)
            recorder.Delete(file);

        return recorder.Directory;
    }

    /// <summary>
    /// Zips the selected parts of the game's user-data folder. Files that cannot be read (e.g. locked
    /// by the running game) are skipped and returned.
    /// </summary>
    public static IReadOnlyList<string> CreateBackup(string gameDataPath, string zipPath,
        bool includeTray, bool includeSaves, bool includeMods, IProgress<string>? progress = null)
    {
        var folders = new List<string>();
        if (includeTray) folders.Add("Tray");
        if (includeSaves) folders.Add("saves");
        if (includeMods) folders.Add(ModsFolderLocator.ModsFolderName);

        var skipped = new List<string>();
        string tempZip = zipPath + ".tmp";
        using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
        {
            foreach (string folderName in folders)
            {
                string folder = Path.Combine(gameDataPath, folderName);
                if (!Directory.Exists(folder))
                    continue;

                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(gameDataPath, file).Replace('\\', '/');
                    progress?.Report(relative);
                    try
                    {
                        // Tray/Mods content is mostly compressed already; don't burn time recompressing it.
                        zip.CreateEntryFromFile(file, relative, folderName == "saves" ? CompressionLevel.Optimal : CompressionLevel.Fastest);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skipped.Add(relative);
                    }
                }
            }
        }

        File.Move(tempZip, zipPath, overwrite: true);
        return skipped;
    }
}
