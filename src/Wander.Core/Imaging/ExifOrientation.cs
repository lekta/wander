namespace Wander.Core.Imaging;

/// <summary>
/// An EXIF orientation value (1..8) as the turn that sets the stored picture
/// upright: a horizontal mirror first, then a clockwise turn - the order of
/// a WPF transform group and of WinRT's <c>BitmapTransform</c> alike. One
/// rule for the pane, RAW thumbnails and conversion: 5 and 7 were swapped in
/// all three (stand 2026-09-25), and <see cref="AfGeometry"/> is the check.
/// </summary>
public static class ExifOrientation {
    /// <summary>Null, 1 and anything outside 1..8 leave the picture as it is.</summary>
    public static (bool Mirror, int Clockwise) Upright(int? orientation) {
        return orientation switch {
            2 => (true, 0),
            3 => (false, 180),
            4 => (true, 180),
            5 => (true, 270),
            6 => (false, 90),
            7 => (true, 90),
            8 => (false, 270),
            _ => (false, 0),
        };
    }
}
