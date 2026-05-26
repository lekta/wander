using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// Visible when <c>value.ToString()</c> equals the converter parameter, Collapsed otherwise.
/// Works with any enum (or any value where ToString is the discriminator).
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is null || parameter is not string target) {
            return Visibility.Collapsed;
        }
        return string.Equals(value.ToString(), target, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
