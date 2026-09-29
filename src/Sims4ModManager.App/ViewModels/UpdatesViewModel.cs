using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
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
