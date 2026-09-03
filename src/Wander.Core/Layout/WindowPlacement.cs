namespace Wander.Core.Layout;

/// <summary>
/// A rectangle in screen coordinates, in the plain numbers Core is allowed
/// to know about - the virtual desktop on the way in, the restored window
/// on the way out.
/// </summary>
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height) {
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}


/// <summary>
/// Where a window saved in a previous session may be put back.
///
/// <para>
/// Two things can be wrong with saved geometry, and both are silent: a size
/// small enough that the window has no titlebar to grab, and a position on
/// a monitor that is no longer connected. Neither is visible while it
/// works, and the second one loses the window entirely - so the arithmetic
/// lives here where a test reaches it rather than in the window that
/// applies it.
/// </para>
/// </summary>
public static class WindowPlacement {
    /// <summary>
    /// Below this the window is a stub with nothing to grab; a saved size
    /// that small is a truncation, not a choice, and is ignored.
    /// </summary>
    public const double MinWidth = 320;

    public const double MinHeight = 240;

    /// <summary>How much of the titlebar has to stay reachable by the mouse.</summary>
    private const double VisibleWidth = 100;

    private const double VisibleHeight = 60;


    /// <summary>Is a saved size worth restoring, or is it a truncation?</summary>
    public static bool IsUsableSize(double width, double height) {
        return width >= MinWidth && height >= MinHeight;
    }


    /// <summary>
    /// The saved position moved the least it can be so that a grabbable
    /// strip of the window stays on the virtual desktop. A window saved on
    /// a monitor that is gone comes back on one that is there.
    /// </summary>
    /// <param name="saved">Position and size as they were saved.</param>
    /// <param name="screen">The virtual desktop - all monitors as one rectangle.</param>
    public static (double Left, double Top) Clamp(ScreenRect saved, ScreenRect screen) {
        double minLeft = screen.Left - saved.Width + VisibleWidth;
        double maxLeft = screen.Right - VisibleWidth;
        double minTop = screen.Top;
        double maxTop = screen.Bottom - VisibleHeight;

        return (
            Math.Min(Math.Max(saved.Left, minLeft), maxLeft),
            Math.Min(Math.Max(saved.Top, minTop), maxTop));
    }


    /// <summary>
    /// Where a plaque that follows the cursor goes: below and to the right
    /// of it, flipped to the other side of the cursor on whichever axis
    /// there is no room on, and never off the screen.
    ///
    /// <para>
    /// The flip is the whole point. The drag plaque says what would happen
    /// on release, and the one place a drag most often ends up — the
    /// taskbar at the bottom of the screen — is exactly where a plaque
    /// hung below the cursor falls off the edge and the user drags blind.
    /// Above the cursor there is always room, because the cursor got to the
    /// bottom of the screen by coming down from it.
    /// </para>
    /// </summary>
    /// <param name="offset">Gap between the cursor and the plaque, on both axes.</param>
    public static (double Left, double Top) BesideCursor(
        double cursorX, double cursorY, double width, double height, ScreenRect screen, double offset) {
        double left = cursorX + offset;
        if (left + width > screen.Right) {
            left = cursorX - offset - width;
        }

        double top = cursorY + offset;
        if (top + height > screen.Bottom) {
            top = cursorY - offset - height;
        }

        // A plaque wider or taller than the screen cannot be placed
        // properly; putting its top-left corner on screen at least keeps
        // the beginning of the text readable.
        return (
            Math.Min(Math.Max(left, screen.Left), Math.Max(screen.Left, screen.Right - width)),
            Math.Min(Math.Max(top, screen.Top), Math.Max(screen.Top, screen.Bottom - height)));
    }
}
