using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Sims4ModManager.App.Services;

/// <summary>Switches the WPF-UI theme (dark/light) for the whole application.</summary>
public static class ThemeService
{
    public static void Apply(bool dark) =>
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);
}
