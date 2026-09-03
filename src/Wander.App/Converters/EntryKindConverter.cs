using System.Globalization;
using System.Windows.Data;
using Wander.App.Resources;
using Wander.Core.FileSystem;

namespace Wander.App.Converters;

/// <summary>
/// The table's "Тип" column: an <see cref="EntryKind"/> as a word from the
/// resources. Bound straight to the enum, the column printed the member
/// names - "File", "Directory" - in the middle of a Russian interface, and
/// check-strings had no way to notice.
/// </summary>
public sealed class EntryKindConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value switch {
            EntryKind.Directory => Strings.ColumnTypeFolder,
            EntryKind.Drive => Strings.ColumnTypeDrive,
            EntryKind.File => Strings.ColumnTypeFile,
            _ => null,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
