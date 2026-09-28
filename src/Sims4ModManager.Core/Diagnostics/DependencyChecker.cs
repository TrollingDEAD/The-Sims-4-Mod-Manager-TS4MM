using System.IO.Compression;
using System.Text;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Diagnostics;

/// <summary>A shared library other mods build on.</summary>
/// <param name="ModulePrefix">Python package the library ships (identifies the library itself).</param>
/// <param name="Marker">Text that appears in scripts or tuning of mods that need it.</param>
public sealed record KnownLibrary(string Name, string ModulePrefix, string Marker, string DownloadHint);

public enum DependencyState { Missing, Disabled }

public sealed record DependencyProblem(KnownLibrary Library, DependencyState State,
    IReadOnlyList<ModFileInfo> Dependents, IReadOnlyList<ModEntry> DependentMods, ModEntry? LibraryMod);

/// <summary>
/// Finds mods that need a shared library (Sims4CommunityLib, XML Injector, Lot 51 Core Library)
/// which is missing or disabled - the "missing requirements" check of TS4 Mod Hound, done locally.
/// Scripts are searched for the library's module name (kept as plain text in Python bytecode),
/// tuning resources for the library's tuning classes.
/// </summary>
public static class DependencyChecker
{
    public static readonly IReadOnlyList<KnownLibrary> Libraries = new[]
    {
        new KnownLibrary(L.T("Sims4CommunityLib (S4CL)"), "sims4communitylib", "sims4communitylib", L.T("CurseForge: „Sims4CommunityLib“ von ColonolNutty")),
        new KnownLibrary(L.T("XML Injector"), "xml_injector", "xml_injector", L.T("Mod The Sims / scumbumbomods.com: „XML Injector“")),
        new KnownLibrary(L.T("Lot 51 Core Library"), "lot51_core", "lot51_core", L.T("lot51.cc: „Core Library“")),
    };

    /// <summary>Tuning types in which libraries are referenced (snippets, generic tuning, combined tuning).</summary>
    private static readonly HashSet<uint> TuningTypes = new() { 0x7DF2169C, 0x0333406C, 0x03B33DDF, 0x62E94D38 };

    public static IReadOnlyList<DependencyProblem> Check(IReadOnlyList<ModEntry> mods)
    {
        var problems = new List<DependencyProblem>();
        foreach (var library in Libraries)
        {
            var providers = mods
                .Where(m => m.Files.Any(f => f.Kind == ModFileKind.Script && f.ScriptModules.Any(s => s.ModulePath.StartsWith(library.ModulePrefix + "/", StringComparison.Ordinal))))
                .ToList();

            var dependents = mods
                .Where(m => !providers.Contains(m))
                .SelectMany(m => m.Files.Where(f => f.IsEnabled).Select(f => (Mod: m, File: f)))
                .Where(x => Mentions(x.File, library.Marker))
                .ToList();
            if (dependents.Count == 0)
                continue;

            bool providerEnabled = providers.Any(p => p.IsEnabled || p.IsPartiallyEnabled);
            if (providerEnabled)
                continue;

            problems.Add(new DependencyProblem(library,
                providers.Count == 0 ? DependencyState.Missing : DependencyState.Disabled,
                dependents.Select(d => d.File).ToList(),
                dependents.Select(d => d.Mod).Distinct().ToList(),
                providers.FirstOrDefault()));
        }
        return problems;
    }

    private static bool Mentions(ModFileInfo file, string marker)
    {
        try
        {
            return file.Kind switch
            {
                ModFileKind.Script => ScriptMentions(file.AbsolutePath, marker),
                ModFileKind.Package => TuningMentions(file, marker),
                _ => false
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ScriptMentions(string path, string marker)
    {
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            if (Encoding.Latin1.GetString(memory.GetBuffer(), 0, (int)memory.Length).Contains(marker, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool TuningMentions(ModFileInfo file, string marker)
    {
        var tuning = file.Resources.Where(r => TuningTypes.Contains(r.Key.Type)).ToList();
        if (tuning.Count == 0)
            return false;

        using var stream = File.OpenRead(file.AbsolutePath);
        foreach (var resource in tuning)
        {
            byte[] data = Saves.SaveGameReader.ReadResource(stream, resource);
            if (Encoding.Latin1.GetString(data).Contains(marker, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
