using System.Collections.Concurrent;
using System.IO;
using Sims4ModManager.Core.Tray;

namespace Sims4ModManager.App.Services;

/// <summary>
/// Watches the Windows Downloads folder and reports new Sims 4 downloads (mods, tray files, or
/// archives containing them) once the browser has finished writing them.
/// </summary>
public sealed class DownloadWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);

    public DownloadWatcher()
    {
        Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(Folder))
            return;

        _watcher = new FileSystemWatcher(Folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Created += (_, e) => Consider(e.FullPath);
        _watcher.Renamed += (_, e) => Consider(e.FullPath); // browsers rename "x.crdownload" to "x.zip" when done
    }

    public string Folder { get; }

    /// <summary>Raised (on a background thread) with the path of a finished Sims 4 download.</summary>
    public event Action<string>? DownloadDetected;

    public bool Enabled
    {
        get => _watcher?.EnableRaisingEvents ?? false;
        set
        {
            if (_watcher is not null)
                _watcher.EnableRaisingEvents = value;
        }
    }

    private void Consider(string path)
    {
        if (!TrayInstaller.IsArchive(path) && !Core.ModFileNaming.IsManagedModFile(Path.GetFileName(path)) && !TrayFileName.HasTrayExtension(path))
            return;
        if (!_pending.TryAdd(path, 0))
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                if (await WaitUntilStableAsync(path) && TrayInstaller.LooksLikeSimsDownload(path))
                    DownloadDetected?.Invoke(path);
            }
            finally
            {
                _pending.TryRemove(path, out _);
            }
        });
    }

    /// <summary>Waits until the file size stops changing and it can be opened (download finished).</summary>
    private static async Task<bool> WaitUntilStableAsync(string path)
    {
        long last = -1;
        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (!File.Exists(path))
                return false;
            long size = new FileInfo(path).Length;
            if (size == last && size > 0)
            {
                try
                {
                    using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return true;
                }
                catch (IOException) { /* still being written */ }
            }
            last = size;
        }
        return false;
    }

    public void Dispose() => _watcher?.Dispose();
}
