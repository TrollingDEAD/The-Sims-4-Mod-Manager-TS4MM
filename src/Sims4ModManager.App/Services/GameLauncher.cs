using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Sims4ModManager.Core.Game;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.Services;

/// <summary>Finds the installed game and starts it.</summary>
public static class GameLauncher
{
    private const string ExecutableRelative = @"Game\Bin\TS4_x64.exe";

    /// <summary>Folder the user chose in the "Spiel" tab; wins over detection.</summary>
    public static string? PreferredInstallFolder { get; set; }

    /// <summary>Install folder: the user's choice, else the registry (EA app/Origin, uninstall entries) or the default Steam path.</summary>
    public static string? FindInstallFolder()
    {
        if (PreferredInstallFolder is { } preferred && Directory.Exists(Path.Combine(preferred, "Data")))
            return preferred;

        foreach (var (hive, key) in new[]
                 {
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Maxis\The Sims 4"),
                     (Registry.LocalMachine, @"SOFTWARE\Maxis\The Sims 4")
                 })
        {
            if (hive.OpenSubKey(key)?.GetValue("Install Dir") is string dir && HasExecutable(dir))
                return dir;
        }

        foreach (string uninstallRoot in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var root = Registry.LocalMachine.OpenSubKey(uninstallRoot);
            if (root is null)
                continue;
            foreach (string name in root.GetSubKeyNames())
            {
                using var entry = root.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is string display && display.Trim().Equals("The Sims 4", StringComparison.OrdinalIgnoreCase)
                    && entry.GetValue("InstallLocation") is string location && HasExecutable(location))
                    return location;
            }
        }

        string steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steamapps\common\The Sims 4");
        return HasExecutable(steam) ? steam : null;
    }

    public static string? FindExecutable() => FindInstallFolder() is { } dir ? Path.Combine(dir, ExecutableRelative) : null;

    private static bool HasExecutable(string dir) => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, ExecutableRelative));

    /// <summary>Starts the game; returns an error message or null.</summary>
    public static string? Launch()
    {
        string? exe = FindExecutable();
        if (exe is null)
            return L.T("Die Sims 4-Installation wurde nicht gefunden.");
        try
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    /// <summary>Waits until the game has started and ended again (polling, the launcher may start the real process later).</summary>
    public static async Task WaitForGameSessionAsync(CancellationToken cancel)
    {
        var startDeadline = DateTime.UtcNow.AddMinutes(3);
        while (!GameInfo.IsGameRunning())
        {
            if (DateTime.UtcNow > startDeadline)
                return;
            await Task.Delay(TimeSpan.FromSeconds(3), cancel);
        }
        while (GameInfo.IsGameRunning())
            await Task.Delay(TimeSpan.FromSeconds(5), cancel);
    }
}
