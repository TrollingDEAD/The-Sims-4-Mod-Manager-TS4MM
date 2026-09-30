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

    /// <summary>
    /// Downscales every enabled package's oversized CAS/object textures (larger than
    /// <see cref="TextureTools.RecommendedMaxDimension"/> on either axis) to a box-filtered, mip-
    /// complete copy at the recommended size - many CC creators ship 4K textures the game never
    /// resolves at typical camera distances. Resources whose compression BCnEncoder.Net doesn't
    /// recognize are left untouched rather than guessed at ("Speicherplatz" tab).
    /// </summary>
    public async Task DownscaleTexturesAsync()
    {
        var candidates = CurrentMods.SelectMany(m => m.Files)
            .Where(f => f.Kind == ModFileKind.Package && f.IsEnabled)
            .Select(f => (File: f, Textures: TextureTools.Inspect(f.AbsolutePath, f.Resources, TextureTools.RecommendedMaxDimension)))
            .Where(x => x.Textures.Count > 0)
            .ToList();
        if (candidates.Count == 0)
        {
            StatusMessage = L.T("Keine überdimensionierten Texturen gefunden.");
            return;
        }

        int textureCount = candidates.Sum(c => c.Textures.Count);
        if (!await EnsureGameClosedAsync() || !await _dialogs.ConfirmAsync(L.T("Texturen verkleinern"),
                L.F("{0} Texturen über {1}×{1} Pixel in {2} Packages werden auf die jeweils nächstpassende Zweierpotenz verkleinert (neue Mip-Kette, gleiches Kompressionsformat). ", textureCount, TextureTools.RecommendedMaxDimension, candidates.Count) +
                L.T("Das Spiel löst mehr Detail bei üblichem Kameraabstand ohnehin nicht auf; die Bildqualität aus der Nähe kann sichtbar sinken. Nicht erkannte Formate bleiben unverändert.") +
                Environment.NewLine + Environment.NewLine +
                L.T("Die Originale kommen in die Sicherung (Tab „Verlauf“) – der Platz wird erst frei, wenn diese Sicherung gelöscht wird.") + Environment.NewLine + UndoHint,
                L.T("Verkleinern"), destructive: true))
            return;

        long saved = 0;
        int downscaled = 0, skipped = 0;
        var errors = new List<string>();
        await Task.Run(() =>
        {
            using var recorder = Journal.Begin(L.F("{0} Texturen verkleinert", textureCount));
            foreach (var (file, textures) in candidates)
            {
                try
                {
                    var keys = textures.Select(t => t.Key).ToHashSet();
                    recorder.Replace(file.AbsolutePath, temp =>
                    {
                        var result = TextureTools.Downscale(file.AbsolutePath, temp, keys, TextureTools.RecommendedMaxDimension);
                        saved += result.Saved;
                        downscaled += result.Downscaled;
                        skipped += result.Skipped;
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    errors.Add($"{Path.GetFileName(file.AbsolutePath)}: {ex.Message}");
                }
            }
        });
        StatusMessage = L.F("{0} Textur(en) verkleinert, {1} gespart.", downscaled, Formatting.Size(saved)) +
                        (skipped > 0 ? " " + L.F("{0} nicht unterstützte(s) Format(e) übersprungen.", skipped) : "") +
                        (errors.Count > 0 ? " " + L.F("{0} Fehler: {1}", errors.Count, string.Join("; ", errors.Take(2))) : "") + " " + UndoHint;
        AfterChange();
    }

    /// <summary>
    /// Downscales every enabled package's oversized catalog thumbnails (larger than
    /// <see cref="ThumbnailTools.RecommendedMaxDimension"/> on either axis) - some creators embed
    /// unnecessarily large preview images. Thumbnails that carry the undocumented "ALFA" transparency
    /// segment are left untouched, since this codebase can only decode that scheme, not safely rebuild
    /// it ("Speicherplatz" tab).
    /// </summary>
    public async Task DownscaleThumbnailsAsync()
    {
        var candidates = CurrentMods.SelectMany(m => m.Files)
            .Where(f => f.Kind == ModFileKind.Package && f.IsEnabled)
            .Select(f => (File: f, Thumbnails: ThumbnailTools.Inspect(f.AbsolutePath, f.Resources, ThumbnailTools.RecommendedMaxDimension)))
            .Where(x => x.Thumbnails.Count > 0)
            .ToList();
        if (candidates.Count == 0)
        {
            StatusMessage = L.T("Keine überdimensionierten Vorschaubilder gefunden.");
            return;
        }

        int thumbnailCount = candidates.Sum(c => c.Thumbnails.Count);
        if (!await EnsureGameClosedAsync() || !await _dialogs.ConfirmAsync(L.T("Vorschaubilder verkleinern"),
                L.F("{0} Katalog-Vorschaubilder über {1}×{1} Pixel in {2} Packages werden verkleinert. ", thumbnailCount, ThumbnailTools.RecommendedMaxDimension, candidates.Count) +
                L.T("Das Spiel zeigt sie im Katalog ohnehin nur sehr klein an; nur Vorschaubilder ohne Transparenz (z. B. die meisten Build/Buy-Objekte) werden angefasst.") +
                Environment.NewLine + Environment.NewLine +
                L.T("Die Originale kommen in die Sicherung (Tab „Verlauf“) – der Platz wird erst frei, wenn diese Sicherung gelöscht wird.") + Environment.NewLine + UndoHint,
                L.T("Verkleinern")))
            return;

        long saved = 0;
        int downscaled = 0, skipped = 0;
        var errors = new List<string>();
        await Task.Run(() =>
        {
            using var recorder = Journal.Begin(L.F("{0} Vorschaubilder verkleinert", thumbnailCount));
            foreach (var (file, thumbnails) in candidates)
            {
                try
                {
                    var keys = thumbnails.Select(t => t.Key).ToHashSet();
                    recorder.Replace(file.AbsolutePath, temp =>
                    {
                        var result = ThumbnailTools.Downscale(file.AbsolutePath, temp, keys, ThumbnailTools.RecommendedMaxDimension);
                        saved += result.Saved;
                        downscaled += result.Downscaled;
                        skipped += result.Skipped;
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    errors.Add($"{Path.GetFileName(file.AbsolutePath)}: {ex.Message}");
                }
            }
        });
        StatusMessage = L.F("{0} Vorschaubild(er) verkleinert, {1} gespart.", downscaled, Formatting.Size(saved)) +
                        (skipped > 0 ? " " + L.F("{0} mit Transparenz übersprungen.", skipped) : "") +
                        (errors.Count > 0 ? " " + L.F("{0} Fehler: {1}", errors.Count, string.Join("; ", errors.Take(2))) : "") + " " + UndoHint;
        AfterChange();
    }
}
