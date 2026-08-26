using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// Row-level switch between the name label and the inline rename editor.
/// values[0] is the row's own full path, values[1] is
/// <c>MainViewModel.RenamingPath</c>; the editor is Visible only on the row
/// being renamed. ConverterParameter "Inverse" flips the answer, which is
/// how the label hides itself while the editor is up.
/// </summary>
public sealed class RenameEditorVisibilityConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) {
        bool editing = values.Length == 2
            && values[0] is string path
            && values[1] is string renaming
            && string.Equals(path, renaming, StringComparison.OrdinalIgnoreCase);
        bool inverse = parameter as string == "Inverse";

        return editing != inverse ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
