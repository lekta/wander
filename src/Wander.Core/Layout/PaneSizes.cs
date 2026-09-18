namespace Wander.Core.Layout;

/// <summary>
/// How wide (or tall) a side pane comes back at when the window is not the
/// size it was when the pane was last dragged.
///
/// <para>
/// The case this exists for is a laptop after a monitor: a preview pane
/// left at 748 px of a 1925 px window came back at 748 px in a 1086 px
/// window, and the file list got thirty pixels. Rescaling by the width the
/// pane had of the window it was saved from keeps the proportion the user
/// chose; a window that is the size it was gets the pane back exactly, to
/// the pixel, because seventy per cent of the window may well be what they
/// wanted.
/// </para>
/// </summary>
public static class PaneSizes {
    /// <summary>
    /// Upper bound while the window size is not known at all - before the
    /// window is up. Once it is, the bound is the window less the reserve.
    /// </summary>
    public const double LegacyMax = 900;

    /// <summary>Window sizes this close together count as the same size.</summary>
    private const double SameWindow = 1;


    /// <param name="savedPane">The pane size that was persisted.</param>
    /// <param name="savedWindow">Window size at the time it was persisted; 0 when the file predates it.</param>
    /// <param name="currentWindow">Window size now.</param>
    /// <param name="min">Smallest the pane may be.</param>
    /// <param name="reserve">How much of the window has to be left for everything else.</param>
    public static double Restore(double savedPane, double savedWindow, double currentWindow, double min, double reserve) {
        if (currentWindow <= 0) {
            return Clamp(savedPane, min, LegacyMax);
        }
        if (savedWindow <= 0) {
            // A file from before the window size was kept beside the pane:
            // nothing to scale by, so the pane comes back as it was - but
            // never wider than leaves the rest its room. The fixed ceiling
            // that stood here let a 900 px pane into a 1086 px window, and
            // cut a wider one on a monitor that had the room (2026-09-18).
            return Clamp(savedPane, min, Math.Max(min, currentWindow - reserve));
        }
        if (Math.Abs(savedWindow - currentWindow) <= SameWindow) {
            return savedPane;
        }

        return Clamp(savedPane * currentWindow / savedWindow, min, Math.Max(min, currentWindow - reserve));
    }


    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
