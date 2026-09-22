using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Wander.App.Resources;

namespace Wander.App.Converters;

/// <summary>
/// The colour of a sharpness score (<see cref="Wander.Core.Imaging.Sharpness.Score"/>)
/// by level: sharp, soft, missed. The bounds come from the stand of
/// 2026-09-22 - two R8 frames scored 79 and 81, the same frames blurred by
/// a pixel 55-60, by two 20-30 - and are the first calibration, not a
/// verdict.
/// </summary>
public sealed class SharpnessLevelConverter : IValueConverter {
    public const double Sharp = 70;
    public const double Soft = 45;


    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value switch {
            double score when score >= Sharp => Palette.ReviewSharpGood,
            double score when score >= Soft => Palette.ReviewSharpSoft,
            double => Palette.ReviewSharpMissed,
            _ => DependencyProperty.UnsetValue,
        };
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}


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
