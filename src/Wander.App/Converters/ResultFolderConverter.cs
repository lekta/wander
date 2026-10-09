using System.Globalization;
using System.Windows.Data;
using Wander.Core.FileSystem;
using Wander.Core.Search;

namespace Wander.App.Converters;

/// <summary>
/// A search result's folder, said from the folder the search started in
/// (<see cref="ResultPath.Folder"/>): the results' "Folder" column. The
/// root is asked at each row rather than set once - the results take the
/// list over before the search says where it started.
/// </summary>
public sealed class ResultFolderConverter : IValueConverter {
    /// <summary>The folder the results are said from; null says them in full.</summary>
    public Func<string?> Root { get; set; } = () => null;


    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is FileSystemEntry entry ? ResultPath.Folder(Root(), entry.FullPath) : null;
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
