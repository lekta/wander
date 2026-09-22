namespace Wander.Core.Imaging;

/// <summary>
/// Autofocus areas turned with the picture. The camera records them over
/// the frame as the sensor stored it; the pane draws the frame turned by
/// its EXIF orientation, and the areas have to turn the same way.
/// </summary>
public static class AfGeometry {
    /// <summary>
    /// The areas as they lie over the frame shown upright. Orientation is
    /// the EXIF value 1..8; null and anything else leave the areas as they are.
    /// </summary>
    public static IReadOnlyList<AfPoint> Orient(IReadOnlyList<AfPoint> points, int? orientation) {
        return points.Select(p => Orient(p, orientation)).ToArray();
    }



    // EXIF: 2 mirror, 3 half turn, 4 flip, 5 transpose, 6 quarter turn
    // clockwise, 7 transverse, 8 quarter turn counter-clockwise.
    private static AfPoint Orient(AfPoint p, int? orientation) {
        return orientation switch {
            2 => p with { X = 1 - p.X },
            3 => p with { X = 1 - p.X, Y = 1 - p.Y },
            4 => p with { Y = 1 - p.Y },
            5 => p with { X = p.Y, Y = p.X, W = p.H, H = p.W },
            6 => p with { X = 1 - p.Y, Y = p.X, W = p.H, H = p.W },
            7 => p with { X = 1 - p.Y, Y = 1 - p.X, W = p.H, H = p.W },
            8 => p with { X = p.Y, Y = 1 - p.X, W = p.H, H = p.W },
            _ => p,
        };
    }
}
