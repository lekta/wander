namespace Wander.Core.Preview;

/// <summary>
/// Where things stand on a sound file's tile (decision 2026-10-27, variant
/// E): the shape above, a band along the bottom - the play button at its
/// left, the length after it, the rate at the right. The tile is drawn once
/// into the thumbnail and the live button is drawn over it by the list, so
/// both take the places from here and cannot drift apart.
///
/// <para>
/// All in the units of whatever is being drawn: the page (the framed square
/// inside a tile's edge) for the thumbnail, the tile itself for the button
/// over it - <see cref="ButtonOnTile"/> takes the edge off first.
/// </para>
/// </summary>
public static class SoundTile {
    /// <summary>The empty edge round the framed page, of the tile's side - the cover frame's.</summary>
    public const double EdgeShare = 1.0 / 12;

    /// <summary>The band's height, of the page's side.</summary>
    public const double BandShare = 0.26;

    /// <summary>The button's diameter, of the band's height.</summary>
    public const double ButtonShare = 0.8;

    /// <summary>Below this side a tile has no room for a band button; the button covers the icon.</summary>
    public const double SmallestBanded = 64;


    /// <summary>Top and height of the band on a page of side <paramref name="page"/>.</summary>
    public static (double Top, double Height) Band(double page) {
        double height = page * BandShare;

        return (page - height, height);
    }

    /// <summary>The button's centre and radius on a page of side <paramref name="page"/>.</summary>
    public static (double X, double Y, double Radius) Button(double page) {
        var (top, height) = Band(page);
        double radius = height * ButtonShare / 2;
        double inset = (height - radius * 2) / 2;

        return (inset + radius, top + height / 2, radius);
    }

    /// <summary>Where the length starts on a page - just past the button.</summary>
    public static double LengthLeft(double page) {
        var (x, _, radius) = Button(page);

        return x + radius + Band(page).Height * 0.16;
    }

    /// <summary>
    /// The button on a whole tile of side <paramref name="side"/>, edge
    /// included; on a tile too small for a band, a disc over most of it.
    /// </summary>
    public static (double X, double Y, double Radius) ButtonOnTile(double side) {
        if (side < SmallestBanded) {
            return (side / 2, side / 2, Math.Max(5.5, side * 0.4));
        }

        double edge = side * EdgeShare;
        var (x, y, radius) = Button(side - edge * 2);

        return (edge + x, edge + y, radius);
    }
}
