using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Sims4ModManager.App.Services;

/// <summary>Shows a "#RRGGBB" accent preset as a solid color swatch.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
