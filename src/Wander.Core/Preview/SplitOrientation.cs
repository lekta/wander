namespace Wander.Core.Preview;

/// <summary>A picture's width and height as shown - turned upright; only the proportion is read.</summary>
public readonly record struct PictureShape(double Width, double Height);


/// <summary>
/// Which way two pictures share one place (2026-09-23): one above the other
/// or side by side - whichever shows them bigger, from the shape of the place
/// and of the pictures. Landscape frames usually go one above the other and
/// portrait ones side by side, but not always: in a narrow strip even
/// portraits are bigger stacked, in a wide window even landscapes side by
/// side. A picture of unknown shape counts as square, which gives the old
/// rule - stacked while the place is taller than it is wide.
/// </summary>
public static class SplitOrientation {
    /// <summary>
    /// How much bigger the other way has to show the two before a split on
    /// screen turns over: a splitter dragged across the point where both ways
    /// are even must not flip the pictures back and forth under the cursor.
    /// </summary>
    public const double TurnAbove = 1.1;


    /// <summary>True for one above the other, false for side by side.</summary>
    /// <param name="width">What the two pictures share, in any unit.</param>
    /// <param name="height">Same.</param>
    /// <param name="first">The first picture's shape, or null when it is not known.</param>
    /// <param name="second">Same for the second.</param>
    /// <param name="stacked">How the two are split now; null when they are not on screen yet.</param>
    public static bool Stacked(double width, double height, PictureShape? first, PictureShape? second, bool? stacked) {
        if (width <= 0 || height <= 0) {
            return stacked ?? height >= width;
        }

        double a = Aspect(first);
        double b = Aspect(second);
        double above = Fitted(width, height / 2, a) + Fitted(width, height / 2, b);
        double beside = Fitted(width / 2, height, a) + Fitted(width / 2, height, b);

        return stacked switch {
            true => beside <= above * TurnAbove,
            false => above > beside * TurnAbove,
            null => above >= beside,
        };
    }


    private static double Aspect(PictureShape? shape) {
        return shape is { Width: > 0, Height: > 0 } s ? s.Width / s.Height : 1;
    }

    /// <summary>The area a picture of <paramref name="aspect"/> covers fitted into a box.</summary>
    private static double Fitted(double boxWidth, double boxHeight, double aspect) {
        double width = Math.Min(boxWidth, boxHeight * aspect);

        return width * (width / aspect);
    }
}
