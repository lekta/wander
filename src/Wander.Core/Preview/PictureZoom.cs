namespace Wander.Core.Preview;

/// <summary>
/// The zoom of a picture past its fitted size (decision of 2026-10-15):
/// <c>Ctrl</c>+wheel steps fit -> 50 -> 100 -> 200 -> 400 %, and
/// <c>Ctrl</c>+arrows move the place a pinned zoom looks at.
///
/// <para>
/// A scale is frame pixels to screen pixels, 1 being 1:1. The fitted
/// picture is a mode, not a level: it is whatever scale fits the pane, and
/// a level no bigger than that is no step - the picture is shown that big
/// already. The place is a share of the picture area, 0..1 each way, as the
/// pane maps it onto the picture: 0 shows the picture's left edge, 1 its
/// right one.
/// </para>
/// </summary>
public static class PictureZoom {
    /// <summary>The levels past the fitted picture, smallest first.</summary>
    public static IReadOnlyList<double> Levels { get; } = new[] { 0.5, 1.0, 2.0, 4.0 };

    /// <summary>A level within this share of the fitted scale is that scale: no visible step.</summary>
    private const double Slop = 1.02;

    /// <summary>How far one press of <c>Ctrl</c>+arrow moves the view: a third of it.</summary>
    public const double NudgePart = 1.0 / 3;


    /// <summary>
    /// The level <paramref name="notches"/> steps from <paramref name="current"/>
    /// - up for a positive count - or null for the fitted picture.
    /// </summary>
    /// <param name="current">The level on show; null when the picture is fitted.</param>
    /// <param name="fit">The scale the fitted picture is drawn at; 1 for one that is not shrunk.</param>
    /// <param name="notches">Wheel notches, up positive.</param>
    public static double? Step(double? current, double fit, int notches) {
        var levels = Levels.Where(level => level > fit * Slop).ToList();
        // The level on show among the ones past this fit; a level the
        // picture has outgrown (fit bigger since) counts as the fitted one.
        int at = current is { } scale ? levels.FindLastIndex(level => level <= scale * Slop) : -1;
        int to = Math.Clamp(at + notches, -1, levels.Count - 1);

        return to < 0 ? null : levels[to];
    }

    /// <summary>
    /// Where the view goes when <c>Ctrl</c>+arrow moves it a third of
    /// itself along one axis.
    /// </summary>
    /// <param name="share">The place now, 0..1.</param>
    /// <param name="view">The size of the picture area along the axis.</param>
    /// <param name="picture">The size of the zoomed picture along the axis; one no bigger than the view does not move.</param>
    /// <param name="direction">+1 towards the far edge (right, down), -1 back.</param>
    public static double Nudge(double share, double view, double picture, int direction) {
        if (picture <= view || view <= 0) {
            return share;
        }

        // The picture's offset is share * (picture - view): a third of the
        // view is this much of the share.
        double step = NudgePart * view / (picture - view);

        return Math.Clamp(share + Math.Sign(direction) * step, 0, 1);
    }
}
