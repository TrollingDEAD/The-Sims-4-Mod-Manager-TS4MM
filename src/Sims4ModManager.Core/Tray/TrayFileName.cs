using System.Globalization;
using System.Text.RegularExpressions;

namespace Sims4ModManager.Core.Tray;

/// <summary>
/// Parsed name of a file in the Tray folder: "0x{group:8 hex}!0x{instance:16 hex}.{extension}".
/// All files of one library item share the instance (sim portraits use the sim's id instead).
/// </summary>
public readonly record struct TrayFileName(uint Group, ulong Instance, string Extension)
{
    public const string TrayItemExtension = "trayitem";

    /// <summary>Extensions the game writes to the Tray folder.</summary>
    public static readonly IReadOnlySet<string> KnownExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "trayitem", "householdbinary", "hhi", "sgi", "blueprint", "bpi", "room", "rmi"
    };

    /// <summary>Extensions holding item data (as opposed to thumbnails) - these are scanned for CC references.</summary>
    public static readonly IReadOnlySet<string> DataExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "trayitem", "householdbinary", "blueprint", "room"
    };

    private static readonly Regex Pattern = new(
        @"^0x(?<group>[0-9a-f]{8})!0x(?<instance>[0-9a-f]{16})\.(?<ext>[a-z]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string fileName, out TrayFileName result)
    {
        var match = Pattern.Match(fileName);
        if (!match.Success || !KnownExtensions.Contains(match.Groups["ext"].Value))
        {
            result = default;
            return false;
        }

        result = new TrayFileName(
            uint.Parse(match.Groups["group"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            ulong.Parse(match.Groups["instance"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            match.Groups["ext"].Value.ToLowerInvariant());
        return true;
    }

    /// <summary>True for any file the game would treat as tray data (by extension), even if misnamed.</summary>
    public static bool HasTrayExtension(string fileName) =>
        KnownExtensions.Contains(Path.GetExtension(fileName).TrimStart('.'));
}
