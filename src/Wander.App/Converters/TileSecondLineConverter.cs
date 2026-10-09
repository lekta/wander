using System.Globalization;
using System.IO;
using System.Windows.Data;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.FileSystem;
using Wander.Core.Search;

namespace Wander.App.Converters;

/// <summary>
/// The second line of a tile: what the file is in a folder listing - its
/// extension and size, as the table's columns say them, and the word for
/// a folder - its stars, once it has any (J4, decision B28: no new visual
/// in the tile, and a photograph's kind is not news) - and its folder in a
/// search result, from the folder the search started in
/// (<see cref="ResultPath.Folder"/>; empty right in it): results come from
/// everywhere, and the path says more than the kind would.
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

    /// <summary>The folder the search started in, asked at each tile (<see cref="ResultFolderConverter.Root"/>).</summary>
    public Func<string?> Root { get; set; } = () => null;


    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not FileSystemEntry entry) {
            return null;
        }
        if (ShowFolder) {
            return ResultPath.Folder(Root(), entry.FullPath);
        }

        int rank = entry.Rating?.Rank ?? 0;
        if (rank > 0) {
            return new string('★', Math.Min(rank, 5));
        }

        return entry.Kind switch {
            EntryKind.File => FileLine(entry),
            EntryKind.Directory => Strings.KindFolderNoun,
            _ => null,
        };
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }


    private static string FileLine(FileSystemEntry entry) {
        string type = Path.GetExtension(entry.Name).TrimStart('.').ToLowerInvariant();
        string? size = entry.Size is { } bytes ? SizeFormatter.Format(bytes) : null;

        return type.Length > 0 && size is not null ? string.Format(Strings.TileFileLine, type, size) : size ?? type;
    }
}
