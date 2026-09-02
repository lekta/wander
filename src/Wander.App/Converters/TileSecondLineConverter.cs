using System.Globalization;
using System.Windows.Data;
using Wander.Core.FileSystem;

namespace Wander.App.Converters;

/// <summary>
/// The second line of a tile: the file's kind in a folder listing, its
/// folder in a search result - results come from everywhere, and "File"
/// repeated down the column says nothing the path would not say better.
///
/// <para>
/// One converter over the row instead of a Style with a DataTrigger on
/// <c>MainViewModel.IsSearchResults</c>: that trigger cost every tile a
/// RelativeSource binding up to the view and a Style of its own, and the
/// mode it switches on changes only when the whole list is replaced. So
/// the mode is a flag the view sets when the results come and go
/// (<see cref="ShowFolder"/>), and each new tile reads it once.
/// </para>
/// </summary>
public sealed class TileSecondLineConverter : IValueConverter {
    /// <summary>True while the list is showing search results.</summary>
    public bool ShowFolder { get; set; }


    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not FileSystemEntry entry) {
            return null;
        }

        return ShowFolder ? entry.ParentFolder : entry.Kind.ToString();
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
