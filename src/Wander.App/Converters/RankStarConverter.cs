using System.Globalization;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// Star N of a rating widget: filled when the current rank reaches it,
/// hollow otherwise. Value is the rank, parameter is the star's position
/// ("1".."5") — the same parameter the button passes to
/// <c>Preview.SetRankCommand</c>, so the two never drift apart.
/// </summary>
public sealed class RankStarConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is not int rank || parameter is not string raw || !int.TryParse(raw, out int star)) {
            return "☆";
        }

        return rank >= star ? "★" : "☆";
    }


    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
