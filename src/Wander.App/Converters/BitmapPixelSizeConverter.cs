using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Wander.App.Converters;

/// <summary>
/// The size a picture is drawn at, at most: its pixels as device pixels -
/// used to cap preview images at 100 % via <c>MaxWidth</c> /
/// <c>MaxHeight</c> bindings.
///
/// Why XAML-binding instead of code-behind: a <c>DependencyPropertyDescriptor</c>
/// hook on <c>Image.SourceProperty</c> fires after WPF's first measure pass
/// for that Source, so the layout briefly sees an unconstrained Image and
/// stretches small bitmaps. A binding participates in measure directly -
/// the cap is in place the first time WPF asks for the desired size, no race.
///
/// <para>
/// Two values: the pixels (a bitmap, or a number of pixels across or down)
/// and the display's scale (<c>PreviewPane.DpiScale</c>). Pixels taken as
/// layout units drew the picture half as big again at 150 % and softened it
/// (PLAN AM, 2026-09-21); the scale comes as a binding so a move to another
/// monitor redoes the cap.
/// </para>
///
/// ConverterParameter selects the dimension: "W" → PixelWidth, "H" → PixelHeight.
/// Returns <see cref="double.PositiveInfinity"/> when no source is set so
/// the Image stays unconstrained until something loads.
/// </summary>
public sealed class BitmapPixelSizeConverter : IMultiValueConverter {
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) {
        bool height = parameter as string == "H";
        double pixels = values.Length > 0 ? values[0] switch {
            BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } bs => height ? bs.PixelHeight : bs.PixelWidth,
            double d when d > 0 => d,
            _ => double.PositiveInfinity,
        } : double.PositiveInfinity;
        double scale = values.Length > 1 && values[1] is double s && s > 0 ? s : 1;

        return pixels / scale;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
