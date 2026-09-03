using System.Globalization;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// Whether this row is the one the keyboard would move from - Explorer's
/// focus rectangle, and the row an arrow key starts counting at.
///
/// <para>
/// Two bindings rather than a flag on the row, for the same reason
/// <see cref="CutRowConverter"/> is one: the caret is something the window
/// knows, not something the file is, and putting it on
/// <c>FileSystemEntry</c> would replace two rows on every arrow press - the
/// list rebuilding itself in answer to a key that moved nothing.
/// </para>
/// </summary>
public sealed class CaretRowConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) {
        return values.Length >= 2
            && values[0] is string path
            && values[1] is string caret
            && string.Equals(path, caret, StringComparison.OrdinalIgnoreCase);
    }


    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
