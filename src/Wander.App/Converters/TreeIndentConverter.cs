using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// A panel line's depth as a left margin. The panel is a plain list
/// (decision P3): the row's highlight spans the whole width, the way
/// Explorer draws it and what the eye follows down a deep tree, and only
/// its content is indented - by the depth the model gives the line.
/// </summary>
public sealed class TreeIndentConverter : IValueConverter {
    /// <summary>Pixels per level.</summary>
    private const double Step = 16;


    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return new Thickness((value is int depth ? depth : 0) * Step, 0, 0, 0);
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
