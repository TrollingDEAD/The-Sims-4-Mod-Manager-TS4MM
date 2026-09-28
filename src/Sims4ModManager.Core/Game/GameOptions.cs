using System.Text;
using System.Text.RegularExpressions;
using Sims4ModManager.Core.Backup;

namespace Sims4ModManager.Core.Game;

/// <summary>
/// The mod-related switches in the game's Options.ini ("[options]" section, "key = value" lines).
/// Patches regularly turn mods off again - one of the most common "my mods don't load" causes.
/// </summary>
public sealed class GameOptions
{
    public const string FileName = "Options.ini";
    public const string ModsDisabledKey = "modsdisabled";
    public const string ScriptModsEnabledKey = "scriptmodsenabled";
    public const string ShowModListKey = "showmodliststartup";

    private GameOptions(IReadOnlyDictionary<string, string> values) => Values = values;

    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>"Benutzerdefinierte Inhalte und Mods aktivieren" (stored inverted as modsdisabled).</summary>
    public bool? ModsEnabled => Flag(ModsDisabledKey) is { } disabled ? !disabled : null;

    /// <summary>"Skript-Mods erlaubt".</summary>
    public bool? ScriptModsEnabled => Flag(ScriptModsEnabledKey);

    /// <summary>The list of installed CC shown at game start (slows down loading).</summary>
    public bool? ShowModListAtStartup => Flag(ShowModListKey);

    private bool? Flag(string key) =>
        Values.TryGetValue(key, out var value) && int.TryParse(value, out int number) ? number != 0 : null;

    public static string PathFor(string gameDataFolder) => Path.Combine(gameDataFolder, FileName);

    /// <summary>Reads Options.ini; null if it does not exist (game never started) or cannot be read.</summary>
    public static GameOptions? TryLoad(string gameDataFolder)
    {
        try
        {
            string path = PathFor(gameDataFolder);
            if (!File.Exists(path))
                return null;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && !line.TrimStart().StartsWith('['))
                    values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            return new GameOptions(values);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Changes the given keys (value 0/1) through the journal. Only the values are replaced -
    /// every other byte of the file (other settings, line endings) stays as it is. Missing keys are
    /// appended to the [options] section.
    /// </summary>
    public static void Update(string gameDataFolder, IReadOnlyDictionary<string, bool> changes, ChangeRecorder recorder)
    {
        string path = PathFor(gameDataFolder);
        string text = File.ReadAllText(path, Encoding.UTF8);
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";

        foreach (var (key, value) in changes)
        {
            string number = value ? "1" : "0";
            var pattern = new Regex(@"^(?<prefix>[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*)(?<value>[^\r\n]*)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (pattern.IsMatch(text))
                text = pattern.Replace(text, m => m.Groups["prefix"].Value + number, 1);
            else
                text = text.TrimEnd('\r', '\n') + newline + $"{key} = {number}" + newline;
        }

        recorder.Replace(path, temp => File.WriteAllText(temp, text, new UTF8Encoding(false)));
    }
}
