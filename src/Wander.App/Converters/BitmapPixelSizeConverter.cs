using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Wander.App.Converters;

/// <summary>
/// Returns the source bitmap's native pixel size as a double, used to cap
/// preview images at 100 % via <c>MaxWidth</c> / <c>MaxHeight</c> bindings.
///
/// Why XAML-binding instead of code-behind: a <c>DependencyPropertyDescriptor</c>
/// hook on <c>Image.SourceProperty</c> fires after WPF's first measure pass
/// for that Source, so the layout briefly sees an unconstrained Image and
/// stretches small bitmaps. Binding through this converter on
/// <c>{Binding Source, RelativeSource=Self}</c> participates in measure
/// directly — the cap is in place the first time WPF asks for the desired
/// size, no race.
///
/// ConverterParameter selects the dimension: "W" → PixelWidth, "H" → PixelHeight.
/// Returns <see cref="double.PositiveInfinity"/> when no source is set so
/// the Image stays unconstrained until something loads.
/// </summary>
public sealed class BitmapPixelSizeConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not BitmapSource bs || bs.PixelWidth <= 0 || bs.PixelHeight <= 0) {
            return double.PositiveInfinity;
        }
        string p = parameter as string ?? "W";
        return p == "H" ? (double)bs.PixelHeight : (double)bs.PixelWidth;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
