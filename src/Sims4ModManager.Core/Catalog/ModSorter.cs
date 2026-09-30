using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Health;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Catalog;

public sealed class SortOptions
{
    /// <summary>Put mods into a subfolder per creator ("CAS - Haare\Creator").</summary>
    public bool ByCreator { get; set; } = true;

    /// <summary>Also move mod folders (sets), not only loose files.</summary>
    public bool IncludeFolders { get; set; } = true;

    /// <summary>Also re-sort mods that already live in a collection folder.</summary>
    public bool Resort { get; set; }
}

/// <summary>What sorting knows about a mod.</summary>
public sealed record SortInput(ModEntry Mod, ContentCategory Category, CasCategory CasCategory, string? Creator);

public sealed record SortMove(ModEntry Mod, string TargetFolder, string CategoryLabel);

public sealed record SortSkip(ModEntry Mod, string Reason);

public sealed record SortPlan(string ModsPath, IReadOnlyList<SortMove> Moves, IReadOnlyList<SortSkip> Skipped)
{
    public int FileCount => Moves.Sum(m => m.Mod.Files.Count);
}

public sealed record SortResult(int MovedMods, IReadOnlyList<string> Errors);

/// <summary>
/// Sorts the Mods folder into collection folders by content ("CAS - Haare", "Objekte", "Skript-Mods" …)
/// and optionally creator. Collections are marked with <see cref="ModScanner.CollectionMarker"/>, so each
/// mod stays its own entry (and keeps its ID - profiles and notes survive). The game's depth rules are
/// respected: script files stay at most one folder deep, packages at most five.
/// </summary>
public static class ModSorter
{
    public const string ScriptFolder = "Skript-Mods";

    internal const string MarkerText =
        "Sammelordner des Sims 4 Mod Managers: jede Datei und jeder Unterordner hier ist ein eigener Mod.\r\n" +
        "Wird diese Datei gelöscht, gilt der Ordner wieder als ein einziger Mod. Das Spiel ignoriert sie.\r\n";

    public static SortPlan Plan(string modsPath, IReadOnlyList<SortInput> inputs, SortOptions options)
    {
        var moves = new List<SortMove>();
        var skipped = new List<SortSkip>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Loose script files and the packages that belong to them ("mc_cmd_center.ts4script" + "mc_cmd_center.package").
        var looseScripts = inputs.Where(i => !i.Mod.IsFolder && i.Mod.ContainsScript).Select(i => i.Mod.DisplayName).ToList();
        var scriptStems = looseScripts.Select(Stem).Where(s => s.Length >= 3)
            .Concat(looseScripts).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs.OrderBy(i => i.Mod.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var mod = input.Mod;
            if (mod.Collection.Length > 0 && !options.Resort)
            {
                skipped.Add(new SortSkip(mod, L.T("bereits einsortiert")));
                continue;
            }
            if (mod.IsFolder && !options.IncludeFolders)
            {
                skipped.Add(new SortSkip(mod, L.T("Ordner (Sets) werden nicht verschoben")));
                continue;
            }
            if (mod.IsFolder && mod.ContainsScript)
            {
                skipped.Add(new SortSkip(mod, L.T("Skript-Mod-Ordner bleibt, wo er ist (.ts4script darf höchstens 1 Ordner tief liegen)")));
                continue;
            }

            bool belongsToScript = !mod.IsFolder && (mod.ContainsScript
                || (input.Category == ContentCategory.Gameplay && (scriptStems.Contains(Stem(mod.DisplayName)) || scriptStems.Contains(mod.DisplayName))));
            string categoryFolder = belongsToScript ? ScriptFolder : FolderName(input.Category, input.CasCategory);
            string target = belongsToScript || !options.ByCreator || string.IsNullOrWhiteSpace(input.Creator)
                ? categoryFolder
                : Path.Combine(categoryFolder, SafeFolderName(input.Creator!));

            if (string.Equals(mod.Collection, target, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new SortSkip(mod, L.T("liegt schon im richtigen Ordner")));
                continue;
            }

            int targetDepth = target.Split(Path.DirectorySeparatorChar).Length;
            int innerDepth = mod.IsFolder ? 1 + mod.Files.Max(f => f.RelativePathInMod.Count(c => c == Path.DirectorySeparatorChar)) : 0;
            if (targetDepth + innerDepth > ModFolderHealth.MaxPackageDepth)
            {
                skipped.Add(new SortSkip(mod, L.T("Ordnerstruktur wäre zu tief (das Spiel lädt höchstens 5 Ordner tief)")));
                continue;
            }

            string name = Path.GetFileName(mod.AbsolutePath);
            string destination = Path.Combine(modsPath, target, name);
            if (!taken.Add(destination) || File.Exists(destination) || Directory.Exists(destination))
            {
                skipped.Add(new SortSkip(mod, L.F("im Zielordner „{0}“ gibt es schon „{1}“", target, name)));
                continue;
            }

            moves.Add(new SortMove(mod, target, belongsToScript ? ScriptFolder : ContentCategories.Label(input.Category, input.CasCategory)));
        }

        return new SortPlan(modsPath, moves, skipped);
    }

    public static SortResult Apply(SortPlan plan, ChangeRecorder recorder)
    {
        var errors = new List<string>();
        int moved = 0;
        string markerSource = Path.Combine(recorder.Directory, "sammelordner.txt");
        Directory.CreateDirectory(recorder.Directory);
        File.WriteAllText(markerSource, MarkerText);

        foreach (var move in plan.Moves)
        {
            try
            {
                string targetDir = Path.Combine(plan.ModsPath, move.TargetFolder);
                EnsureCollection(plan.ModsPath, move.TargetFolder, markerSource, recorder);

                if (move.Mod.IsFolder)
                {
                    string destination = Path.Combine(targetDir, Path.GetFileName(move.Mod.AbsolutePath));
                    foreach (var file in move.Mod.Files)
                        recorder.Move(file.AbsolutePath, Path.Combine(destination, file.RelativePathInMod));
                    RemoveEmptyFolders(move.Mod.AbsolutePath, recorder);
                }
                else
                {
                    string file = move.Mod.Files[0].AbsolutePath;
                    recorder.Move(file, Path.Combine(targetDir, Path.GetFileName(file)));
                }
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{move.Mod.DisplayName}: {ex.Message}");
            }
        }
        return new SortResult(moved, errors);
    }

    /// <summary>Creates each level of <paramref name="relativeFolder"/> as a collection (folder + marker).</summary>
    internal static void EnsureCollection(string modsPath, string relativeFolder, string markerSource, ChangeRecorder recorder)
    {
        string current = modsPath;
        foreach (string part in relativeFolder.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            recorder.CreateDirectory(current);
            string marker = Path.Combine(current, ModScanner.CollectionMarker);
            if (!File.Exists(marker))
                recorder.CopyIn(markerSource, marker);
        }
    }

    /// <summary>Removes the (now empty) folders of a moved folder mod, deepest first; leftovers like readmes stay.</summary>
    internal static void RemoveEmptyFolders(string root, ChangeRecorder recorder)
    {
        if (!Directory.Exists(root))
            return;
        foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            recorder.DeleteEmptyDirectory(dir);
        recorder.DeleteEmptyDirectory(root);
    }

    public static string FolderName(ContentCategory category, CasCategory cas) =>
        category == ContentCategory.Cas && cas != CasCategory.None
            ? L.F("CAS - {0}", ContentCategories.Label(cas))
            : ContentCategories.Label(category);

    /// <summary>First word of a name ("mc_cmd_center" → "mc", "MCCC Main" → "MCCC").</summary>
    private static string Stem(string name) => name.Split('_', '-', ' ', '.')[0];

    private static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return clean.Length == 0 ? "_" : clean;
    }
}
