using System.Text.RegularExpressions;

namespace Sims4ModManager.Core.Catalog;

/// <summary>
/// Guesses the creator of a mod from naming conventions: "[Creator] …", "(Creator) …", "… by Creator",
/// "…~Creator", the prefix Sims 4 Studio puts in front of CAS part names ("Creator_yfHair_…"), and a
/// leading word shared by several files ("AggressiveKitty Door …", "AggressiveKitty Window …").
/// </summary>
public static partial class CreatorGuesser
{
    public sealed record Input(string Key, string Name, IReadOnlyList<string> CasPartNames);

    private static readonly HashSet<string> NotCreators = new(StringComparer.OrdinalIgnoreCase)
    {
        "hair", "hairs", "lipstick", "lips", "eyes", "eye", "eyeliner", "eyeshadow", "eyebrows", "brows", "blush", "lashes",
        "top", "tops", "bottom", "bottoms", "dress", "set", "cc", "tsr", "mod", "mods", "the", "new", "my", "sims", "sims4",
        "ts4", "s4", "default", "merged", "pose", "poses", "female", "male", "kids", "kid", "child", "toddler", "adults",
        "adult", "halloween", "christmas", "skin", "skintone", "overlay", "tattoo", "tattoos", "nails", "shoes", "jacket",
        "pants", "jeans", "shirt", "earrings", "necklace", "ring", "glasses", "hat", "freckles", "makeup", "no", "remove",
        "better", "more", "custom", "simple", "basic", "mini", "big", "small", "long", "short", "yf", "ym", "yu", "cf", "cm",
        "cu", "pf", "pm", "pu", "af", "am", "au", "ef", "em", "eu", "tf", "tm", "tu", "acc", "cas", "build", "buy", "object",
        "objects", "clutter", "deco", "decor", "wall", "floor", "door", "window", "script", "tuning", "fix", "patch"
    };

    [GeneratedRegex(@"^\s*[\[\(\{](?<c>[^\]\)\}]{2,32})[\]\)\}]")]
    private static partial Regex BracketPrefix();

    [GeneratedRegex(@"\bby[\s_\-]+(?<c>[A-Za-z0-9][\w\-\.']{1,30})", RegexOptions.IgnoreCase)]
    private static partial Regex ByCreator();

    [GeneratedRegex(@"~\s*(?<c>[A-Za-z][\w\-\.]{1,30})\s*$")]
    private static partial Regex TildeSuffix();

    [GeneratedRegex(@"^(?<c>[A-Za-z][A-Za-z0-9\.']{1,30})(?=[_\s\-])")]
    private static partial Regex LeadingWord();

    public static IReadOnlyDictionary<string, string> Guess(IEnumerable<Input> inputs)
    {
        var list = inputs.ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Leading words shared by at least three mods are likely creator names.
        var leadingCounts = list
            .Select(i => LeadingWord().Match(i.Name))
            .Where(m => m.Success && IsPlausible(m.Groups["c"].Value))
            .GroupBy(m => m.Groups["c"].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var input in list)
        {
            string? creator = FromName(input.Name)
                              ?? FromCasParts(input.CasPartNames)
                              ?? FromLeadingWord(input.Name, leadingCounts);
            if (creator is not null)
                result[input.Key] = creator;
        }

        // One spelling per creator ("BABYETEARS", "Babyetears" → the most common one).
        var spelling = result.Values
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.GroupBy(v => v).OrderByDescending(s => s.Count()).First().Key, StringComparer.OrdinalIgnoreCase);
        foreach (string key in result.Keys.ToList())
            result[key] = spelling[result[key]];
        return result;
    }

    private static string? FromName(string name)
    {
        foreach (var regex in new[] { ByCreator(), BracketPrefix(), TildeSuffix() })
        {
            var match = regex.Match(name);
            if (match.Success)
            {
                string value = Clean(match.Groups["c"].Value);
                if (IsPlausible(value))
                    return value;
            }
        }
        return null;
    }

    /// <summary>Sims 4 Studio prefixes cloned CAS part names with the creator name set in its settings.</summary>
    private static string? FromCasParts(IReadOnlyList<string> names)
    {
        return names
            .Where(n => n.Contains('_'))
            .Select(n => n.Split('_')[0].Split(' ')[0])
            .Select(p => p.Count(c => c == '-') > 1 ? p.Split('-')[0] : p) // "Sentate-Homme-Socks" → "Sentate"
            .Select(Clean)
            .Where(p => IsPlausible(p) && !p.All(char.IsDigit))
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static string? FromLeadingWord(string name, IReadOnlyDictionary<string, int> counts)
    {
        var match = LeadingWord().Match(name);
        if (!match.Success)
            return null;
        string word = match.Groups["c"].Value;
        return counts.TryGetValue(word, out int count) && count >= 3 && IsPlausible(word) ? word : null;
    }

    private static string Clean(string value) => value.Trim().Trim('!', '.', '-', '_', '\'', ' ', '(', ')', '[', ']', '{', '}');

    /// <summary>Two-letter names only as initials ("KK", "TT"), not words like "Oh".</summary>
    private static bool IsPlausible(string value) =>
        (value.Length >= 3 || (value.Length == 2 && value.All(char.IsUpper))) && value.Any(char.IsLetter) && !NotCreators.Contains(value)
        && !value.Any(c => c is ':' or '/' or '\\') && !Regex.IsMatch(value, @"^v\d+$", RegexOptions.IgnoreCase);
}
