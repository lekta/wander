using System.Globalization;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// Returns <c>true</c> when <c>value.ToString()</c> equals the converter parameter.
/// Useful for menu IsChecked bindings against enum properties.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter {
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        return value is not null
            && parameter is string target
            && string.Equals(value.ToString(), target, StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
