using System.Globalization;
using System.IO;
using System.Windows.Data;
using Wander.App.Util;
using Wander.Core.FileSystem;

namespace Wander.App.Converters;

/// <summary>
/// The table's "Тип" column: the extension, as sorting by type orders
/// rows - "cr3", "jpg"; lower case so a column of capitals does not
/// shout. Folders and drives get nothing: the icon already says what
/// they are, and "Файл" / "Папка" on every row was noise.
/// </summary>
public sealed class FileTypeConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is FileSystemEntry { Kind: EntryKind.File } entry
            ? Path.GetExtension(entry.Name).TrimStart('.').ToLowerInvariant()
            : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}


/// <summary>
/// The table's "Размер" column through <see cref="SizeFormatter"/>, as
/// everywhere else: bytes in full are a number to count digits in. Nothing
/// for a folder - it has no size of its own.
/// </summary>
public sealed class FileSizeConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is long bytes ? SizeFormatter.Format(bytes) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
