using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.Core.Localization;
using Velopack;
using Velopack.Sources;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// Checks GitHub Releases for a newer app version and installs it in place (Velopack). Silently does
/// nothing outside an installed (Velopack) build, e.g. when running via "dotnet run" during development.
/// </summary>
public partial class AppUpdateViewModel : ObservableObject
{
    private const string RepoUrl = "https://github.com/TrollingDEAD/The-Sims-4-Mod-Manager-TS4MM";

    private readonly MainViewModel _main;
    private UpdateManager _manager;
    private UpdateInfo? _pending;

    public AppUpdateViewModel(MainViewModel main)
    {
        _main = main;
        includePrereleases = main.Settings.Load().IncludePrereleaseUpdates;
        _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: includePrereleases));
    }

    /// <summary>Include pre-release (beta) builds when checking for an app update; persisted, no restart needed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextChannelLabel), nameof(ChannelToggleTooltip))]
    private bool includePrereleases;

    /// <summary>Label of the channel toggle button: the channel clicking it switches <em>to</em>.</summary>
    public string NextChannelLabel => IncludePrereleases ? L.T("Stable") : L.T("Beta");

    public string ChannelToggleTooltip => IncludePrereleases
        ? L.T("Beta-Updates (Vorabversionen) sind aktiv. Klicken, um zu stabilen Updates zu wechseln.")
        : L.T("Stabile Updates sind aktiv. Klicken, um auch Beta-Updates (Vorabversionen) zu erhalten.");

    partial void OnIncludePrereleasesChanged(bool value)
    {
        _main.Settings.TryUpdate(s => s.IncludePrereleaseUpdates = value);
        _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: value));
        _pending = null;
        IsAvailable = false;
        IsReadyToInstall = false;
        LatestVersion = null;
        _ = CheckAsync();
    }

    [RelayCommand]
    private void ToggleChannel() => IncludePrereleases = !IncludePrereleases;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private bool isAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    private bool isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private bool isReadyToInstall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private string? latestVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private int downloadProgress;

    /// <summary>Label of the title-bar update button; its meaning depends on the current step.</summary>
    public string ActionLabel =>
        IsReadyToInstall ? L.T("Neu starten & installieren")
        : IsDownloading ? L.F("Wird heruntergeladen … {0}%", DownloadProgress)
        : L.F("Update {0} verfügbar", LatestVersion);

    /// <summary>Checks for an update in the background; call once shortly after startup.</summary>
    public async Task CheckAsync()
    {
        if (!_manager.IsInstalled)
            return;
        try
        {
            _pending = await _manager.CheckForUpdatesAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            App.Log(ex);
            return;
        }
        if (_pending is null)
            return;
        LatestVersion = "v" + _pending.TargetFullRelease.Version;
        IsAvailable = true;
        _main.ShowToast(L.F("Update {0} verfügbar.", LatestVersion), ToastKind.Info);
    }

    /// <summary>Downloads the pending update, then (once clicked again) applies it and restarts.</summary>
    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private async Task RunActionAsync()
    {
        if (_pending is null)
            return;
        if (IsReadyToInstall)
        {
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
            return;
        }

        IsDownloading = true;
        try
        {
            await _manager.DownloadUpdatesAsync(_pending, p => DownloadProgress = p);
            IsReadyToInstall = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            App.Log(ex);
            _main.StatusMessage = L.T("Update konnte nicht heruntergeladen werden.");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private bool CanRunAction() => !IsDownloading;
}
