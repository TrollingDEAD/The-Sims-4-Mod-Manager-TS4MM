using System.IO;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.App.ViewModels;

/// <summary>Package tools: merge, unmerge, optimize - every change goes through the journal.</summary>
public partial class MainViewModel
{
    [RelayCommand]
    private void OpenMerge()
    {
        if (ModsPath is null)
            return;
        var window = new MergeWindow(new MergeViewModel(this, _dialogs)) { Owner = System.Windows.Application.Current.MainWindow };
        window.ShowDialog();
    }

    /// <summary>Removes duplicate index entries and compresses uncompressed resources of one package.</summary>
    public async Task OptimizePackageAsync(ModFileInfo file)
    {
        if (!await EnsureGameClosedAsync())
            return;
        try
        {
            var result = await Task.Run(() =>
            {
                OptimizeResult? r = null;
                using var recorder = Journal.Begin(L.F("Package optimiert: {0}", Path.GetFileName(file.AbsolutePath)));
                recorder.Replace(file.AbsolutePath, temp => r = PackageTools.Optimize(file.AbsolutePath, temp));
                return r!;
            });
            StatusMessage = L.F("{0} optimiert: {1} doppelte Einträge entfernt, {2} Ressourcen komprimiert, {3} gespart.",
                Path.GetFileName(file.AbsolutePath), result.DuplicatesRemoved, result.Compressed, Formatting.Size(result.Saved)) + " " + UndoHint;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusMessage = L.F("Optimieren fehlgeschlagen: {0}", ex.Message);
        }
        AfterChange();
    }

    /// <summary>Splits a merged package into its original files, in a folder next to it.</summary>
    public async Task UnmergePackageAsync(ModFileInfo file)
    {
        var parts = PackageMerger.PlanUnmerge(file.AbsolutePath);
        if (parts is null || parts.Count == 0)
        {
            StatusMessage = L.T("Dieses Package enthält keine Liste der Originaldateien und lässt sich nicht zerlegen.");
            return;
        }
        string name = Path.GetFileNameWithoutExtension(ModFileNaming.ToEnabledPath(file.AbsolutePath));
        string targetDir = Path.Combine(Path.GetDirectoryName(file.AbsolutePath)!, name);
        if (!await EnsureGameClosedAsync() || !await _dialogs.ConfirmAsync(L.T("Package zerlegen"),
                L.F("„{0}“ wird in {1} Originaldateien zerlegt. Sie landen im Ordner „{2}“, das zusammengeführte Package wird entfernt.", name, parts.Count, targetDir) +
                Environment.NewLine + UndoHint, L.T("Zerlegen")))
            return;

        bool disabled = !file.IsEnabled;
        var errors = new List<string>();
        await Task.Run(() =>
        {
            using var recorder = Journal.Begin(L.F("Zerlegt: {0} ({1} Dateien)", name, parts.Count));
            foreach (var part in parts)
            {
                string target = Path.Combine(targetDir, part.FileName);
                if (disabled)
                    target = ModFileNaming.ToDisabledPath(target);
                try { recorder.Create(target, temp => PackageMerger.WritePart(file.AbsolutePath, part, temp)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { errors.Add($"{part.FileName}: {ex.Message}"); }
            }
            if (errors.Count == 0)
                recorder.Delete(file.AbsolutePath);
        });
        StatusMessage = errors.Count == 0
            ? L.F("„{0}“ in {1} Dateien zerlegt.", name, parts.Count) + " " + UndoHint
            : L.F("Zerlegen unvollständig ({0} Fehler: {1}) – das Original bleibt erhalten.", errors.Count, string.Join("; ", errors.Take(2)));
        AfterChange();
    }

    /// <summary>Compresses every enabled package that stores larger resources uncompressed ("Speicherplatz" tab).</summary>
    public async Task CompressAllAsync()
    {
        var candidates = CurrentMods.SelectMany(m => m.Files)
            .Where(f => f.Kind == ModFileKind.Package && f.IsEnabled)
            .Select(f => (File: f, Inspection: PackageTools.Inspect(f.Resources)))
            .Where(x => x.Inspection.UncompressedBytes >= 64 * 1024 || x.Inspection.DuplicateEntries > 0)
            .ToList();
        if (candidates.Count == 0)
        {
            StatusMessage = L.T("Keine Packages mit nennenswerten unkomprimierten Daten gefunden.");
            return;
        }
        long bytes = candidates.Sum(c => c.Inspection.UncompressedBytes);
        if (!await EnsureGameClosedAsync() || !await _dialogs.ConfirmAsync(L.T("Packages komprimieren"),
                L.F("{0} Packages enthalten {1} unkomprimierte Daten. Sie werden verlustfrei komprimiert (wie „Batch Fix → Compress“ in Sims 4 Studio); der Inhalt im Spiel bleibt gleich.", candidates.Count, Formatting.Size(bytes)) +
                Environment.NewLine + Environment.NewLine +
                L.T("Die Originale kommen in die Sicherung (Tab „Verlauf“) – der Platz wird erst frei, wenn diese Sicherung gelöscht wird.") + Environment.NewLine + UndoHint,
                L.T("Komprimieren")))
            return;

        long saved = 0;
        int done = 0;
        var errors = new List<string>();
        await Task.Run(() =>
        {
            using var recorder = Journal.Begin(L.F("{0} Packages komprimiert", candidates.Count));
            foreach (var (file, _) in candidates)
            {
                try
                {
                    recorder.Replace(file.AbsolutePath, temp => saved += PackageTools.Optimize(file.AbsolutePath, temp).Saved);
                    done++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    errors.Add($"{Path.GetFileName(file.AbsolutePath)}: {ex.Message}");
                }
            }
        });
        StatusMessage = L.F("{0} Packages komprimiert, {1} gespart.", done, Formatting.Size(saved)) +
                        (errors.Count > 0 ? " " + L.F("{0} Fehler: {1}", errors.Count, string.Join("; ", errors.Take(2))) : "") + " " + UndoHint;
        AfterChange();
    }
}
