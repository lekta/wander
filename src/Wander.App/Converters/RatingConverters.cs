using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Wander.App.ViewModels;
using Wander.Core.Companions;
using Wander.Core.FileSystem;

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


/// <summary>
/// Whether a piece of the rating badge has anything to say. Parameter picks
/// which piece: <c>Badge</c> for the badge as a whole, <c>Color</c> for the
/// colour dot inside it.
///
/// <para>
/// The distinction matters because "has a sidecar" and "has a rating" are
/// different things, and the first one is not worth a pixel. A photo whose
/// <c>.pp3</c> exists but says nothing was drawing an empty box in the
/// corner of its thumbnail; a photo with three stars and no colour label
/// was drawing three stars with a hole punched to their left.
/// </para>
/// </summary>
public sealed class RatingBadgeVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        var rating = value as SidecarRating;
        bool show = (parameter as string) == "Color"
            ? (rating?.ColorLabel ?? 0) > 0
            : (rating?.Rank ?? 0) > 0 || (rating?.ColorLabel ?? 0) > 0;

        return show ? Visibility.Visible : Visibility.Collapsed;
    }


    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}


/// <summary>
/// One star of the filter bar, lit or not. Takes the whole
/// <see cref="RatingFilter"/> rather than a number because which stars are
/// lit is a set, not a run: "three and up, but not five" is a thing the bar
/// can say, and the only way to see that it is saying it is star by star.
/// </summary>
public sealed class FilterStarConverter : IValueConverter {
    private const string Filled = "\u2605";
    private const string Hollow = "\u2606";


    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not RatingFilter filter || parameter is not string raw || !int.TryParse(raw, out int star)) {
            return Hollow;
        }

        return filter.HasRank(star) ? Filled : Hollow;
    }


    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
