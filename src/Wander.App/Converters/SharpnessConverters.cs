using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// The sharpness badge on a gallery cell: the score as a number 0..100, or,
/// with the parameter <c>Badge</c>, whether there is a score to show.
/// </summary>
public sealed class SharpnessBadgeConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if ((parameter as string) == "Badge") {
            return value is double ? Visibility.Visible : Visibility.Collapsed;
        }

        return value is double score ? Math.Round(score).ToString(culture) : "";
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
