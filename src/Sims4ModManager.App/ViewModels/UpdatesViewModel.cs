using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Catalog;
using Sims4ModManager.Core.Localization;
using Sims4ModManager.Core.Online;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// The "Updates" tab: finds CurseForge projects in the Mods folder by fingerprint and installs newer
/// releases through the change journal. Mods from other sites (Patreon, Tumblr …) cannot be checked
/// automatically; for those the download link in the mod's notes is the way back to the source.
/// </summary>
public partial class UpdatesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsStore _settings = new();
    private CancellationTokenSource? _cancel;

    public UpdatesViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;
        HasApiKey = SecretProtector.Unprotect(_settings.Load().CurseForgeApiKeyProtected) is not null;
        var last = _settings.Load().LastUpdateCheckUtc;
        Summary = last is null
            ? L.T("Noch nicht geprüft.")
            : L.F("Zuletzt geprüft: {0:g}", last.Value.ToLocalTime());
    }

    public ObservableCollection<CurseForgeModViewModel> Mods { get; } = new();

    [ObservableProperty] private bool hasApiKey;
    [ObservableProperty] private string apiKeyInput = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyText = string.Empty;
    [ObservableProperty] private string summary;
    [ObservableProperty] private int updateCount;
    [ObservableProperty] private bool onlyUpdates;
    [ObservableProperty] private int linkedModCount;

    public string ApiKeyUrl => CurseForgeClient.ApiKeyUrl;

    private IReadOnlyList<InstalledCurseForgeMod> _all = Array.Empty<InstalledCurseForgeMod>();

    /// <summary>Mod IDs with a newer CurseForge release, for the "update available" row icon in the Mods tab.</summary>
    public IReadOnlySet<string> ModIdsWithUpdates => _all.Where(m => m.UpdateAvailable)
        .SelectMany(m => m.LocalFiles.Select(l => l.Mod.Id))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    partial void OnOnlyUpdatesChanged(bool value) => ShowMods();

    [RelayCommand]
    private void SaveApiKey()
    {
        string key = ApiKeyInput.Trim();
        if (key.Length == 0)
            return;
        _settings.TryUpdate(s => s.CurseForgeApiKeyProtected = SecretProtector.Protect(key));
        ApiKeyInput = string.Empty;
        HasApiKey = true;
        _main.StatusMessage = L.T("CurseForge-API-Schlüssel gespeichert (verschlüsselt für dein Windows-Konto).");
    }

    [RelayCommand]
    private void RemoveApiKey()
    {
        _settings.TryUpdate(s => s.CurseForgeApiKeyProtected = null);
        HasApiKey = false;
    }

    [RelayCommand]
    private void OpenApiKeyPage() => OpenUrl(CurseForgeClient.ApiKeyUrl);

    /// <summary>Internal so the CurseForge Browse tab can reuse this VM's key-handling as its single source of truth.</summary>
    internal CurseForgeClient? CreateClient() =>
        SecretProtector.Unprotect(_settings.Load().CurseForgeApiKeyProtected) is { } key ? new CurseForgeClient(key) : null;

    [RelayCommand]
    private async Task CheckAsync()
    {
        var client = CreateClient();
        if (client is null)
        {
            HasApiKey = false;
            return;
        }
        var mods = _main.CurrentMods;
        _cancel = new CancellationTokenSource();
        IsBusy = true;
        BusyText = L.T("Dateien werden erkannt …");
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => BusyText = L.F("Fingerabdrücke berechnen: {0} von {1} Dateien …", p.Done, p.Total));
            var checker = new CurseForgeUpdateChecker(client);
            var result = await checker.CheckAsync(mods, progress, _cancel.Token);
            _all = result.Mods;
            _settings.TryUpdate(s => s.LastUpdateCheckUtc = DateTime.UtcNow);
            ShowMods();
            _main.RefreshModUpdateFlags();
            UpdateCount = result.Mods.Count(m => m.UpdateAvailable);
            LinkedModCount = _main.Mods.Count(m => m.NoteTooltip?.Contains("http", StringComparison.OrdinalIgnoreCase) == true);
            Summary = L.F("{0} von {1} Dateien stammen von CurseForge ({2} Projekte) – {3} Update(s) verfügbar.",
                          result.CheckedFiles - result.UnmatchedFiles, result.CheckedFiles, result.Mods.Count, UpdateCount);
        }
        catch (CurseForgeException ex)
        {
            Summary = ex.Message;
        }
        catch (OperationCanceledException)
        {
            Summary = L.T("Abgebrochen.");
        }
        finally
        {
            IsBusy = false;
            _cancel = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    private static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(12);

    /// <summary>
    /// Runs the update check once in the background shortly after startup - mirrors
    /// <see cref="AppUpdateViewModel.CheckAsync"/>'s silent startup check, but gated behind an API
    /// key (skipped silently without one) and paced by <see cref="AutoCheckInterval"/> instead of
    /// running on every launch, since this is a real CurseForge API call subject to rate limits,
    /// unlike the other tabs' badges which just piggyback on the existing mod-folder rescan.
    /// </summary>
    public async Task CheckIfDueAsync()
    {
        if (!HasApiKey || IsBusy)
            return;
        var last = _settings.Load().LastUpdateCheckUtc;
        if (last is not null && DateTime.UtcNow - last.Value < AutoCheckInterval)
            return;

        await CheckAsync();
        if (UpdateCount > 0)
            _main.ShowToast(L.F("{0} Mod-Update(s) auf CurseForge verfügbar.", UpdateCount), ToastKind.Info);
    }

    private void ShowMods()
    {
        Mods.Clear();
        foreach (var mod in _all.Where(m => !OnlyUpdates || m.UpdateAvailable))
            Mods.Add(new CurseForgeModViewModel(mod));
    }

    [RelayCommand]
    private async Task UpdateModAsync(CurseForgeModViewModel? vm)
    {
        if (vm?.Model.LatestFile is not { } latest || CreateClient() is not { } client)
            return;
        if (!vm.Model.AllowsDownload)
        {
            OpenUrl(vm.Model.WebsiteUrl);
            return;
        }
        if (!await _main.EnsureGameClosedAsync())
            return;
        var old = vm.Model.LocalFiles.Select(l => l.File.AbsolutePath).Distinct().ToList();
        string question = L.F("„{0}“ von {1} auf {2} aktualisieren?", vm.Name, vm.InstalledLabel, vm.LatestLabel) + Environment.NewLine + Environment.NewLine +
                          L.F("Ersetzt werden {0} Datei(en) in {1}.", old.Count, Path.GetDirectoryName(old[0])) + Environment.NewLine + MainViewModel.UndoHint;
        if (!await _dialogs.ConfirmAsync(L.T("Update installieren"), question, L.T("Aktualisieren")))
            return;

        IsBusy = true;
        BusyText = L.F("{0} wird heruntergeladen …", latest.FileName);
        string download = Path.Combine(Path.GetTempPath(), "Sims4ModManager", "download-" + Guid.NewGuid().ToString("N"));
        try
        {
            string? url = latest.DownloadUrl ?? await client.GetDownloadUrlAsync(vm.Model.ModId, latest.Id);
            if (url is null)
            {
                await _dialogs.ShowAsync(L.T("Update installieren"),
                    L.T("Der Ersteller erlaubt Downloads nur auf der CurseForge-Seite. Die Seite wird geöffnet."));
                OpenUrl(vm.Model.WebsiteUrl);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(download)!);
            await client.DownloadAsync(url, download);
            BusyText = L.T("Update wird installiert …");
            var installed = await Task.Run(() =>
            {
                using var recorder = _main.Journal.Begin(L.F("Update: {0} ({1})", vm.Name, latest.DisplayName));
                return CurseForgeUpdateChecker.ApplyUpdate(recorder, old, download, latest.FileName);
            });
            _main.StatusMessage = L.F("„{0}“ aktualisiert: {1} Datei(en) installiert.", vm.Name, installed.Count) + " " + MainViewModel.UndoHint;
            _all = _all.Where(m => m != vm.Model).ToList();
            Mods.Remove(vm);
            UpdateCount = Math.Max(0, UpdateCount - 1);
        }
        catch (Exception ex) when (ex is CurseForgeException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _dialogs.ShowAsync(L.T("Update installieren"), ex.Message);
        }
        finally
        {
            try { File.Delete(download); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp file */ }
            IsBusy = false;
        }
        _main.AfterChange();
    }

    [RelayCommand]
    private void OpenPage(CurseForgeModViewModel? vm) => OpenUrl(vm?.Model.WebsiteUrl);

    private const string ModpackFileFilter = "Sims4ModManager-Modpack (*.s4mmmodpack.json)|*.s4mmmodpack.json|Alle Dateien (*.*)|*.*";

    /// <summary>
    /// Exports the CurseForge mods the last check recognized (by reference - project/file ID, not the
    /// files themselves) as one small file a creator or Discord/forum community can publish; installing
    /// it resolves and downloads every mod and its dependencies via <see cref="ImportModpackAsync"/>.
    /// </summary>
    [RelayCommand]
    private void ExportModpack()
    {
        if (_all.Count == 0)
        {
            _main.StatusMessage = L.T("Noch keine CurseForge-Mods erkannt – zuerst „Jetzt prüfen“ ausführen.");
            return;
        }
        var downloadable = _all.Where(m => m.AllowsDownload).ToList();
        if (downloadable.Count == 0)
        {
            _main.StatusMessage = L.T("Keine der erkannten CurseForge-Mods erlaubt automatische Downloads – ein Modpack wäre leer.");
            return;
        }

        string? path = _dialogs.PickSaveFile(L.T("Modpack exportieren"), L.T(ModpackFileFilter),
            $"Sims4-Modpack-{DateTime.Now:yyyy-MM-dd}.s4mmmodpack.json");
        if (path is null)
            return;

        try
        {
            var manifest = new ModpackManifest
            {
                Title = Path.GetFileNameWithoutExtension(path),
                AppVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
                Mods = downloadable.Select(m => new ModpackEntry(m.ModId, m.InstalledFile.Id, m.Name)).ToList()
            };
            ModpackFile.SaveToFile(manifest, path);
            int skipped = _all.Count - downloadable.Count;
            _main.StatusMessage = L.F("Modpack exportiert: {0} Mod(s).", downloadable.Count) +
                                  (skipped > 0 ? " " + L.F("{0} ohne automatischen Download ausgelassen.", skipped) : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.StatusMessage = L.F("Export fehlgeschlagen: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Installs every mod (and its dependencies) a modpack (see <see cref="ExportModpack"/>) references
    /// by CurseForge project/file ID, through the same download/install/sort pipeline as a manual
    /// Browse install. A mod the exporter's distribution settings now disallow, or one no longer found
    /// on CurseForge at all, is skipped (reported) rather than aborting the whole import.
    /// </summary>
    [RelayCommand]
    private async Task ImportModpackAsync()
    {
        if (_main.ModsPath is not { } modsPath)
            return;
        if (CreateClient() is not { } client)
        {
            HasApiKey = false;
            return;
        }

        string? path = _dialogs.PickFiles(L.T("Modpack installieren"), L.T(ModpackFileFilter)).FirstOrDefault();
        if (path is null)
            return;

        var manifest = ModpackFile.TryLoadFromFile(path);
        if (manifest is null)
        {
            await _dialogs.ShowAsync(L.T("Modpack installieren"),
                L.T("Diese Datei ist kein gültiges Modpack (oder wurde mit einer neueren App-Version exportiert)."));
            return;
        }
        if (!await _main.EnsureGameClosedAsync())
            return;

        IsBusy = true;
        BusyText = L.T("Abhängigkeiten werden ermittelt …");
        var downloaded = new List<(string Path, string FileName)>();
        try
        {
            var mods = (await client.GetModsAsync(manifest.Mods.Select(m => m.ModId).Distinct().ToList())).ToDictionary(m => m.Id);
            var roots = new List<(CurseForgeMod Mod, CurseForgeFile File)>();
            var missing = new List<string>();
            foreach (var entry in manifest.Mods)
            {
                if (mods.TryGetValue(entry.ModId, out var mod))
                    roots.Add((mod, new CurseForgeFile { Id = entry.FileId, ModId = entry.ModId, FileName = entry.DisplayName }));
                else
                    missing.Add(entry.DisplayName);
            }
            if (roots.Count == 0)
            {
                await _dialogs.ShowAsync(L.T("Modpack installieren"), L.T("Keiner der Mods in diesem Modpack wurde auf CurseForge gefunden (gelöscht?)."));
                return;
            }

            var (chain, blocked) = await CurseForgeDependencyResolver.ResolveAsync(client, roots);
            var blockedIds = blocked.Select(b => b.Id).ToHashSet();

            var existingNames = _main.CurrentMods.SelectMany(m => m.Files)
                .Select(f => Path.GetFileName(ModFileNaming.ToEnabledPath(f.AbsolutePath)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var toDownload = chain.Where(c => !blockedIds.Contains(c.ModId) && !existingNames.Contains(c.File.FileName)).ToList();
            int skippedCount = chain.Count(c => !blockedIds.Contains(c.ModId)) - toDownload.Count;

            string warning =
                (blocked.Count > 0 ? Environment.NewLine + L.F("{0} nur auf CurseForge verfügbar (übersprungen): {1}", blocked.Count, string.Join(", ", blocked.Select(m => m.Name))) : "") +
                (missing.Count > 0 ? Environment.NewLine + L.F("{0} nicht mehr auf CurseForge gefunden: {1}", missing.Count, string.Join(", ", missing)) : "");
            if (toDownload.Count == 0)
            {
                await _dialogs.ShowAsync(L.T("Modpack installieren"), L.T("Alles aus diesem Modpack ist schon installiert.") + warning);
                return;
            }
            string title = manifest.Title ?? Path.GetFileNameWithoutExtension(path);
            if (!await _dialogs.ConfirmAsync(L.T("Modpack installieren"),
                    L.F("{0} Datei(en) aus „{1}“ werden heruntergeladen und installiert.", toDownload.Count, title) +
                    warning + Environment.NewLine + Environment.NewLine + MainViewModel.UndoHint, L.T("Installieren")))
                return;

            BusyText = L.T("Wird heruntergeladen …");
            foreach (var item in toDownload)
            {
                string? url = item.File.DownloadUrl ?? await client.GetDownloadUrlAsync(item.ModId, item.File.Id);
                if (url is null)
                    continue;
                string temp = Path.Combine(Path.GetTempPath(), "Sims4ModManager", "modpack-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                await client.DownloadAsync(url, temp);
                downloaded.Add((temp, item.File.FileName));
            }

            BusyText = L.T("Wird installiert und einsortiert …");
            var (installedCount, sortedCount) = await Task.Run(() =>
            {
                using var recorder = _main.Journal.Begin(L.F("Modpack installiert: {0}", title));
                var installedPaths = new List<string>();
                foreach (var (downloadPath, name) in downloaded)
                    installedPaths.AddRange(CurseForgeUpdateChecker.ApplyInstall(recorder, modsPath, downloadPath, name));

                var allMods = ModScanner.Scan(modsPath);
                var installedSet = installedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newMods = allMods.Where(m => m.Files.Any(f => installedSet.Contains(f.AbsolutePath))).ToList();
                if (newMods.Count == 0)
                    return (installedPaths.Count, 0);

                var info = new CatalogScanner().Scan(newMods);
                var (creators, _) = CatalogViewModel.GuessCreators(newMods, info);
                var inputs = newMods.Select(m =>
                {
                    var (category, cas) = ModClassifier.ClassifyMod(m, info);
                    return new SortInput(m, category, cas, creators.GetValueOrDefault(m.Id));
                }).ToList();

                var options = new SortOptions { ByCreator = _main.Settings.Load().SortByCreator };
                var plan = ModSorter.Plan(modsPath, inputs, options);
                var sortResult = ModSorter.Apply(plan, recorder);
                return (installedPaths.Count, sortResult.MovedMods);
            });

            _main.AfterChange();
            string toast = L.F("Modpack installiert: {0} Datei(en).", installedCount);
            if (sortedCount > 0) toast += " " + L.T("Einsortiert.");
            if (skippedCount > 0) toast += " " + L.F("{0} schon vorhanden.", skippedCount);
            _main.ShowToast(toast + " " + MainViewModel.UndoHint, ToastKind.Success);
        }
        catch (Exception ex) when (ex is CurseForgeException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _dialogs.ShowAsync(L.T("Modpack installieren"), ex.Message);
        }
        finally
        {
            foreach (var (downloadPath, _) in downloaded)
            {
                try { File.Delete(downloadPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp file */ }
            }
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowMod(CurseForgeModViewModel? vm)
    {
        if (vm?.Model.LocalFiles.FirstOrDefault().Mod is { } mod)
            _main.ShowMod(mod);
    }

    private static void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}

public sealed class CurseForgeModViewModel
{
    public CurseForgeModViewModel(InstalledCurseForgeMod model)
    {
        Model = model;
        Name = model.Name;
        Authors = model.Authors;
        LocalLabel = string.Join(", ", model.LocalFiles.Select(l => l.Mod.DisplayName).Distinct().Take(3));
        InstalledLabel = $"{model.InstalledFile.DisplayName} ({model.InstalledFile.FileDate.ToLocalTime():d})";
        LatestLabel = model.LatestFile is { } latest ? $"{latest.DisplayName} ({latest.FileDate.ToLocalTime():d})" : "–";
        StatusLabel = model.UpdateAvailable
            ? (model.AllowsDownload ? L.T("Update verfügbar") : L.T("Update verfügbar (nur auf CurseForge)"))
            : L.T("Aktuell");
    }

    public InstalledCurseForgeMod Model { get; }
    public string Name { get; }
    public string Authors { get; }
    public string LocalLabel { get; }
    public string InstalledLabel { get; }
    public string LatestLabel { get; }
    public string StatusLabel { get; }
    public bool UpdateAvailable => Model.UpdateAvailable;
}
