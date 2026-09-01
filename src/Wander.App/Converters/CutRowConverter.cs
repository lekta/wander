using System.Globalization;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// Whether this row is one of the files waiting to be moved — the answer the
/// list dims a row on, the way Explorer does between <c>Ctrl+X</c> and the
/// paste that finishes it.
///
/// <para>
/// A multi-binding rather than a flag on the row: "is on the clipboard" is
/// something the application knows, not something the file is, and putting it
/// on <c>FileSystemEntry</c> would mean replacing every affected row — the
/// list jumping in answer to a keystroke that has not moved anything yet.
/// The row supplies its path, the view model supplies the set, and neither
/// has to be rebuilt when the other changes.
/// </para>
/// </summary>
public sealed class CutRowConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) {
        if (values.Length < 2 || values[0] is not string path
            || values[1] is not IReadOnlyList<string> cut) {
            return false;
        }

        foreach (string candidate in cut) {
            if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }


    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
