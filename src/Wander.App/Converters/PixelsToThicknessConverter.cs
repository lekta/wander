using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// A number of pixels as a uniform <see cref="Thickness"/>.
///
/// <para>
/// Exists for the size previews in settings: the cell margin is stored as
/// one integer, and the preview has to lay itself out with exactly that
/// margin for the picture to be worth anything. Everything else in those
/// previews binds straight to <c>Width</c> / <c>FontSize</c>, which take a
/// number as they are.
/// </para>
/// </summary>
public sealed class PixelsToThicknessConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return new Thickness(ToPixels(value));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return Binding.DoNothing;
    }


    private static double ToPixels(object? value) {
        // A half-typed number in the box next to the preview is not an error
        // worth showing; the preview simply stops moving until it parses.
        return value switch {
            int pixels => Math.Max(0, pixels),
            double pixels => Math.Max(0, pixels),
            string text when int.TryParse(text, out int parsed) => Math.Max(0, parsed),
            _ => 0,
        };
    }
}
