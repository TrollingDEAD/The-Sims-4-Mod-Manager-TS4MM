using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Sims4ModManager.Core.Localization;

/// <summary>
/// UI texts. German is the source language and doubles as the key; other languages come from an
/// embedded dictionary (Localization/en.json: German text → translation). Missing entries fall back
/// to German, so an incomplete translation never shows empty text. English is the default language;
/// German only applies when explicitly selected (settings, or an explicit <see cref="German"/> here).
/// </summary>
public static class L
{
    public const string German = "de";
    public const string English = "en";

    private static Dictionary<string, string> _texts = new();

    public static string Language { get; private set; } = English;

    public static bool IsGerman => Language == German;

    /// <summary>Culture for dates and numbers in the current language.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Switches the language (call once at startup, before any UI is created).</summary>
    public static void Use(string? language)
    {
        Language = language == German ? German : English;
        Culture = CultureInfo.GetCultureInfo(Language == English ? "en-US" : "de-DE");
        _texts = Language == German ? new Dictionary<string, string>() : LoadMutable(Language);
    }

    /// <summary>Translates a German text.</summary>
    public static string T(string german) =>
        german.Length > 0 && _texts.TryGetValue(german, out var text) ? text : german;

    /// <summary>Translates a German format string ("{0} Mods gefunden") and fills it in.</summary>
    public static string F(string germanFormat, params object?[] args) =>
        string.Format(Culture, T(germanFormat), args);

    /// <summary>Exact lookup without fallback (for tests and the UI translator).</summary>
    public static bool TryTranslate(string german, out string text) => _texts.TryGetValue(german, out text!);

    public static IReadOnlyDictionary<string, string> Load(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"Sims4ModManager.Core.Localization.{language}.json");
        if (stream is null)
            return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }

    private static Dictionary<string, string> LoadMutable(string language) => new(Load(language));
}
