using System.Diagnostics;
using System.Text;

namespace Sims4ModManager.Core.Game;

/// <summary>Running game detection and the installed game version.</summary>
public static class GameInfo
{
    /// <summary>Process names of the game (64-bit, legacy DirectX 9 build, old 32-bit build).</summary>
    private static readonly string[] ProcessNames = { "TS4_x64", "TS4_DX9_x64", "TS4" };

    /// <summary>
    /// True while The Sims 4 is running. Changing mods then fails (locked files) or has no effect
    /// until the next start, so file-changing actions warn first.
    /// </summary>
    public static bool IsGameRunning()
    {
        foreach (string name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            bool running = processes.Length > 0;
            foreach (var p in processes)
                p.Dispose();
            if (running)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Version from GameVersion.txt in the user folder (written by the game at start):
    /// a 4-byte length followed by ASCII, e.g. "1.126.73.1030". Null if unknown.
    /// </summary>
    public static string? TryReadGameVersion(string gameDataFolder)
    {
        try
        {
            string path = Path.Combine(gameDataFolder, "GameVersion.txt");
            if (!File.Exists(path))
                return null;

            byte[] data = File.ReadAllBytes(path);
            string text = data.Length > 4 && BitConverter.ToInt32(data, 0) == data.Length - 4
                ? Encoding.ASCII.GetString(data, 4, data.Length - 4)
                : Encoding.ASCII.GetString(data);
            string version = new(text.Where(c => char.IsDigit(c) || c == '.').ToArray());
            return version.Length > 0 ? version.Trim('.') : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>When the game last wrote its version - i.e. the first start after an update.</summary>
    public static DateTime? TryGetVersionTimestampUtc(string gameDataFolder)
    {
        string path = Path.Combine(gameDataFolder, "GameVersion.txt");
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    /// <summary>"1.126.73.1030" → "1.126" (the part patch notes and the community use).</summary>
    public static string ShortVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    /// <summary>
    /// Compares two version strings numerically per segment (so 1.126 &gt; 1.99).
    /// Negative if a &lt; b, 0 if equal, positive if a &gt; b.
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            long va = i < pa.Length && long.TryParse(pa[i], out var x) ? x : 0;
            long vb = i < pb.Length && long.TryParse(pb[i], out var y) ? y : 0;
            if (va != vb)
                return va.CompareTo(vb);
        }
        return 0;
    }
}
