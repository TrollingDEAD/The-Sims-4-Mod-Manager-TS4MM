using System.Text.RegularExpressions;

namespace Sims4ModManager.Core.LoadOrder;

/// <summary>
/// Parses and writes the community's numeric load-order prefix convention (e.g. "000_", "010-",
/// "42 "), which works because digits sort before letters and zero-padded numbers sort in numeric
/// order under plain alphabetical comparison.
/// </summary>
public static partial class LoadOrderNaming
{
    [GeneratedRegex(@"^(\d{1,4})[-_ ]")]
    private static partial Regex PrefixPattern();

    /// <summary>The leading number and separator, and the remaining name, or null if none is present.</summary>
    public static (int Number, string Remainder)? TryParsePrefix(string name)
    {
        var match = PrefixPattern().Match(name);
        if (!match.Success)
            return null;
        return (int.Parse(match.Groups[1].Value), name[match.Length..]);
    }

    /// <summary>The name without any recognized leading numeric prefix (unchanged if there is none).</summary>
    public static string StripPrefix(string name) => TryParsePrefix(name)?.Remainder ?? name;

    /// <summary>Formats <paramref name="rest"/> (already without its old prefix) with a new one, zero-padded to 3 digits.</summary>
    public static string FormatWithPrefix(int number, string rest) => $"{number:000}_{rest}";
}
