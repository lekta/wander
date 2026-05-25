using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Wander.App.ViewModels;

namespace Wander.App.Converters;

/// <summary>
/// Visible when current ViewMode equals ConverterParameter (e.g. "Details" / "LargeIcons"),
/// Collapsed otherwise.
/// </summary>
public sealed class ViewModeToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is ViewMode current && parameter is string targetName) {
            return current.ToString() == targetName ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
