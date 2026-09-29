using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Sims4ModManager.App.Services;

/// <summary>Switches the WPF-UI theme (dark/light) and accent color for the whole application.</summary>
public static class ThemeService
{
    /// <summary>Curated accent presets offered in the picker, "#RRGGBB". Null (not listed here) means "system accent".</summary>
    public static readonly IReadOnlyList<string> AccentPresets = new[]
    {
        "#4C8DFF", "#8B5CF6", "#EC4899", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#14B8A6"
    };

    /// <param name="accentColor">"#RRGGBB", or null/empty/unparsable to follow the Windows accent color.</param>
    public static void Apply(bool dark, string? accentColor)
    {
        var theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        if (TryParseColor(accentColor, out var color))
        {
            // Accent first, then theme (updateAccent: false so this second call doesn't recompute the
            // accent from Windows and overwrite the custom color just set) - some control styles (e.g.
            // the "Primary" Button appearance used by "Play") only pick up the accent color while their
            // theme resources are being (re-)applied; setting the color afterwards left them on the
            // previous accent until the next full app restart.
            ApplicationAccentColorManager.Apply(color, theme);
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
        }
        else
        {
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
        }
    }

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
