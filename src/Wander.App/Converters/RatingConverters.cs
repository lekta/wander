using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Wander.App.ViewModels;
using Wander.Core.Companions;

namespace Wander.App.Converters;

/// <summary>
/// A rating as a short row of filled stars — "★★★" for three, nothing at
/// all for none. Used where the rating is <em>shown</em> rather than edited:
/// the Details column and the badge on a gallery cell. The editable widget
/// in the preview footer is five separate buttons and uses
/// <see cref="RankStarConverter"/> instead, because there each star has to
/// be clickable on its own.
///
/// <para>
/// Empty rather than five hollow stars for an unrated file: a column of
/// "☆☆☆☆☆" down a whole folder is noise that hides the two photographs
/// that do have stars, which is the entire reason the column exists.
/// </para>
/// </summary>
public sealed class RankTextConverter : IValueConverter {
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        int rank = value switch {
            SidecarRating rating => rating.Rank ?? 0,
            int direct => direct,
            _ => 0,
        };

        return rank > 0 ? new string('★', Math.Clamp(rank, 0, 5)) : "";
    }


    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}


/// <summary>
/// The colour of a photo's colour label, as a brush. Transparent when there
/// is none — the dot is laid out either way so a column of them does not
/// jump sideways between rows that have one and rows that do not.
///
/// <para>
/// The palette comes from <see cref="ColorLabelViewModel.CreateChoices"/>,
/// the same five brushes the swatch rows use, so a label looks the same
/// wherever it appears.
/// </para>
/// </summary>
public sealed class ColorLabelBrushConverter : IValueConverter {
    private static readonly IReadOnlyList<ColorLabelViewModel> _palette = ColorLabelViewModel.CreateChoices();


    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        int index = value switch {
            SidecarRating rating => rating.ColorLabel ?? 0,
            int direct => direct,
            _ => 0,
        };

        foreach (var choice in _palette) {
            if (choice.Index == index) {
                return choice.Brush;
            }
        }

        return Brushes.Transparent;
    }


    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}


/// <summary>Visible when the bound value is not null. Collapsed when it is.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }


    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
