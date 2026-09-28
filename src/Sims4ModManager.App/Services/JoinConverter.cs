using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace Sims4ModManager.App.Services;

/// <summary>Shows a list of strings as "a, b, c".</summary>
public sealed class JoinConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable items and not string ? string.Join(", ", items.Cast<object>()) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
