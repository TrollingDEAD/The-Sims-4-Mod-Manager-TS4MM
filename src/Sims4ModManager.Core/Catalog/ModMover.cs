using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Health;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Catalog;

public enum ModMoveStatus { Moved, Skipped }

public sealed record ModMoveResult(ModMoveStatus Status, string? Reason = null)
{
    public bool Success => Status == ModMoveStatus.Moved;
}

/// <summary>
/// Relocates a mod between collection subfolders (or to/from the Mods root) from inside the app,
/// instead of dragging files around in Explorer - the move goes through the same journal as every
/// other change (undoable), and since a mod's <see cref="ModEntry.Id"/> is just its file/folder name
/// (not its path - see <see cref="ModScanner"/>), notes, tags and profile membership all keep
/// pointing at the same mod afterward with nothing extra to update.
/// </summary>
public static class ModMover
{
    /// <summary>
    /// Moves <paramref name="mod"/> into <paramref name="targetCollection"/> (relative to
    /// <paramref name="modsPath"/>; empty moves it directly into the Mods folder). Refuses moves that
    /// would put a mod deeper than the game reads (scripts: 1 folder, packages: 5 - see
    /// <see cref="ModFolderHealth"/>) or collide with an existing file/folder of the same name.
    /// </summary>
    public static ModMoveResult Move(ModEntry mod, string modsPath, string targetCollection, ChangeRecorder recorder)
    {
        string target = targetCollection.Trim().Trim('\\', '/');
        if (string.Equals(mod.Collection, target, StringComparison.OrdinalIgnoreCase))
            return new ModMoveResult(ModMoveStatus.Skipped, L.T("Der Mod liegt schon in diesem Ordner."));

        int targetDepth = target.Length == 0 ? 0 : target.Split('\\', '/').Length;
        int innerDepth = mod.IsFolder ? 1 + mod.Files.Max(f => f.RelativePathInMod.Count(c => c == Path.DirectorySeparatorChar)) : 0;
        int maxDepth = mod.ContainsScript ? ModFolderHealth.MaxScriptDepth : ModFolderHealth.MaxPackageDepth;
        if (targetDepth + innerDepth > maxDepth)
            return new ModMoveResult(ModMoveStatus.Skipped, mod.ContainsScript
                ? L.T("Skript-Mods darf das Spiel nur direkt im Mods-Ordner oder einen Ordner tief laden.")
                : L.T("Die Ordnerstruktur wäre zu tief (das Spiel lädt höchstens 5 Ordner tief)."));

        string targetDir = target.Length == 0 ? modsPath : Path.Combine(modsPath, target);
        string name = Path.GetFileName(mod.AbsolutePath);
        string destination = Path.Combine(targetDir, name);
        if (File.Exists(destination) || Directory.Exists(destination))
            return new ModMoveResult(ModMoveStatus.Skipped, L.F("Im Zielordner gibt es schon „{0}“.", name));

        if (target.Length > 0)
        {
            string markerSource = Path.Combine(recorder.Directory, "sammelordner.txt");
            Directory.CreateDirectory(recorder.Directory);
            File.WriteAllText(markerSource, ModSorter.MarkerText);
            ModSorter.EnsureCollection(modsPath, target, markerSource, recorder);
        }

        if (mod.IsFolder)
        {
            foreach (var file in mod.Files)
                recorder.Move(file.AbsolutePath, Path.Combine(destination, file.RelativePathInMod));
            ModSorter.RemoveEmptyFolders(mod.AbsolutePath, recorder);
        }
        else
        {
            recorder.Move(mod.Files[0].AbsolutePath, destination);
        }
        return new ModMoveResult(ModMoveStatus.Moved);
    }
}
