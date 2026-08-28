using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// <c>true</c> hides, <c>false</c> shows — the mirror of WPF's own
/// <see cref="BooleanToVisibilityConverter"/>.
///
/// <para>
/// Needed by anything whose visibility is driven by a flag named for the
/// opposite state. The alternative is a second property on the view model
/// that is nothing but <c>!Other</c>, and one converter is cheaper than
/// one of those per flag.
/// </para>
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is Visibility.Collapsed or Visibility.Hidden;
    }
}
