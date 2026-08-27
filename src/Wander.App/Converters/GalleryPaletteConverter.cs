using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Wander.Core.Persistence;

namespace Wander.App.Converters;

/// <summary>
/// The gallery's three-step surround, as brushes. Parameter picks which
/// role is wanted: <c>Background</c>, <c>Foreground</c> (the caption under
/// a picture) or <c>Dim</c> (the second line, where there is one).
///
/// <para>
/// A converter rather than three brushes per option in XAML because the
/// roles have to move together: a dark background with the light theme's
/// near-black caption is not a dark theme, it is an unreadable one. Keeping
/// the whole set in one place is also what makes this the first piece of a
/// palette rather than a one-off — see
/// <see cref="GalleryBackground"/>.
/// </para>
///
/// <para>
/// The greys are chosen the way photo viewers choose them: mid grey is
/// around 45% lightness, which biases the eye least when judging a
/// picture, and "dark" stops short of pure black so the edges of a dark
/// photograph stay visible against it.
/// </para>
/// </summary>
public sealed class GalleryPaletteConverter : IValueConverter {
    private static readonly Brush _lightBackground = Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush _greyBackground = Frozen(0x6E, 0x6E, 0x6E);
    private static readonly Brush _darkBackground = Frozen(0x1E, 0x1E, 0x1E);

    private static readonly Brush _darkText = Frozen(0x11, 0x11, 0x11);
    private static readonly Brush _lightText = Frozen(0xEE, 0xEE, 0xEE);
    private static readonly Brush _darkDim = Frozen(0x77, 0x77, 0x77);
    private static readonly Brush _lightDim = Frozen(0xBB, 0xBB, 0xBB);


    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        var background = value as GalleryBackground? ?? GalleryBackground.Grey;
        string role = parameter as string ?? "Background";

        // Grey counts as dark for the text on it: mid grey is dark enough
        // that near-black captions disappear into it.
        bool onDark = background != GalleryBackground.Light;

        return role switch {
            "Foreground" => onDark ? _lightText : _darkText,
            "Dim" => onDark ? _lightDim : _darkDim,
            _ => background switch {
                GalleryBackground.Light => _lightBackground,
                GalleryBackground.Dark => _darkBackground,
                _ => _greyBackground,
            },
        };
    }


    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }


    private static Brush Frozen(byte r, byte g, byte b) {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();

        return brush;
    }
}
