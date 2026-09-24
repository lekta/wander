namespace Wander.Core.Layout;

/// <summary>
/// One cell of a <see cref="TileLayout"/>, in layout units.
/// </summary>
public readonly record struct TileRect(double X, double Y, double Width, double Height) {
    public double Bottom => Y + Height;
}


/// <summary>
/// A cell held where it stands on screen while the grid reflows under it
/// (<see cref="TileLayout.AnchorAt"/>).
/// </summary>
/// <param name="Index">The item.</param>
/// <param name="Y">Its top, from the viewport's top - negative when cut off above.</param>
/// <param name="KeepWhole">The user's cell, wholly on screen: it stays wholly on screen.</param>
public readonly record struct TileAnchor(int Index, double Y, bool KeepWhole);


/// <summary>
/// The arithmetic of a wrap layout with uniform cells: how many columns fit,
/// where each cell sits, how tall the whole thing is, and which slice of it
/// a given scroll offset can see.
///
/// <para>
/// This lives in Core, away from WPF, because it is the part that was
/// getting things wrong and the part that can be tested. The panel that uses
/// it (<c>Wander.App/Controls/VirtualizingWrapPanel</c>) is left with
/// plumbing only: generate containers, measure them, arrange them where this
/// says. Every bug the tile views had was in here — a column count computed
/// from one cell size while the cells were placed at another, a visible
/// range that did not cover the viewport — and none of it needs a window on
/// screen to check.
/// </para>
///
/// <para>
/// The value is immutable and cheap: recomputed from scratch on every
/// layout pass rather than kept in sync by hand, which is exactly the class
/// of bug it exists to prevent.
/// </para>
/// </summary>
public readonly record struct TileLayout {
    /// <summary>
    /// Rows of cells realised past the bottom edge. One is enough to keep a
    /// wheel notch from showing a band of empty cells before the next
    /// layout pass catches up.
    /// </summary>
    private const int OverscanRows = 1;


    public TileLayout(double viewportWidth, double viewportHeight, double cellWidth, double cellHeight, int itemCount) {
        ViewportWidth = Math.Max(0, viewportWidth);
        ViewportHeight = Math.Max(0, viewportHeight);
        // A degenerate cell would divide by zero and, worse, make Columns
        // enormous. One layout unit is nonsense but it is finite nonsense,
        // and the next pass replaces it with a real measurement.
        CellWidth = cellWidth > 0 ? cellWidth : 1;
        CellHeight = cellHeight > 0 ? cellHeight : 1;
        ItemCount = Math.Max(0, itemCount);
    }


    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public double CellWidth { get; }

    public double CellHeight { get; }

    public int ItemCount { get; }


    /// <summary>
    /// Cells per row — at least one, so a viewport narrower than a single
    /// cell still lays out (clipped) instead of dividing by zero.
    /// </summary>
    public int Columns => Math.Max(1, (int)Math.Floor(ViewportWidth / CellWidth));

    public int Rows => ItemCount == 0 ? 0 : (int)Math.Ceiling((double)ItemCount / Columns);

    public double ExtentWidth => Columns * CellWidth;

    public double ExtentHeight => Rows * CellHeight;

    /// <summary>How far down the view can go before it runs out of content.</summary>
    public double MaxVerticalOffset => Math.Max(0, ExtentHeight - ViewportHeight);


    /// <summary>Where the cell for <paramref name="index"/> sits in extent coordinates.</summary>
    public TileRect CellAt(int index) {
        int row = index / Columns;
        int column = index % Columns;

        return new TileRect(column * CellWidth, row * CellHeight, CellWidth, CellHeight);
    }


    /// <summary>
    /// The items worth having containers for at <paramref name="verticalOffset"/>,
    /// as an inclusive index range. <c>Last &lt; First</c> means "nothing" —
    /// an empty list, or an offset past the end.
    /// </summary>
    public (int First, int Last) VisibleRange(double verticalOffset) {
        if (ItemCount == 0) {
            return (0, -1);
        }

        double offset = Clamp(verticalOffset);
        int firstRow = (int)Math.Floor(offset / CellHeight);
        int first = firstRow * Columns;
        if (first >= ItemCount) {
            // Only reachable with a stale offset — the caller clamps, but a
            // shrinking list can outrun it by a frame.
            first = Math.Max(0, (Rows - 1) * Columns);
        }

        int visibleRows = (int)Math.Ceiling(ViewportHeight / CellHeight) + OverscanRows;
        int last = Math.Min(ItemCount - 1, first + (visibleRows * Columns) - 1);

        return (first, last);
    }


    /// <summary>Keeps a scroll offset inside the content.</summary>
    public double Clamp(double verticalOffset) {
        return Math.Max(0, Math.Min(verticalOffset, MaxVerticalOffset));
    }


    /// <summary>
    /// The offset that brings <paramref name="index"/> fully into view,
    /// moving as little as possible — unchanged when the cell already fits.
    /// </summary>
    public double OffsetToReveal(int index, double verticalOffset) {
        var cell = CellAt(index);
        if (cell.Y < verticalOffset) {
            return Clamp(cell.Y);
        }
        if (cell.Bottom > verticalOffset + ViewportHeight) {
            return Clamp(cell.Bottom - ViewportHeight);
        }

        return verticalOffset;
    }


    /// <summary>
    /// This grid, after <paramref name="before"/>, moves the rows under an
    /// unmoved offset: the same items in other columns (a folder panel
    /// shown or hidden, a splitter dragged) or at another cell height
    /// (Ctrl+wheel). The viewport's height alone moves nothing - the top row
    /// stays where it is.
    /// </summary>
    public bool Reflows(TileLayout before) {
        return before.ItemCount > 0 && before.ItemCount == ItemCount && before.ViewportHeight > 0
            && (before.Columns != Columns || Math.Abs(before.CellHeight - CellHeight) > 0.01);
    }

    /// <summary>
    /// The cell to hold in place when the grid reflows (2026-09-23): the one
    /// with the keyboard if any of it shows, else the first selected one that
    /// shows, else the first that shows - what a browser anchors a page on.
    /// Without it the offset stays in pixels, every row below the first
    /// moves, and deep in a folder the whole screen changes. Selected cells
    /// off screen are never pulled in: a reflow is not a reason to scroll.
    /// </summary>
    /// <param name="verticalOffset">Where the grid is scrolled to.</param>
    /// <param name="keyboard">The item with the keyboard, or -1.</param>
    /// <param name="selected">The selected items, in any order.</param>
    public TileAnchor? AnchorAt(double verticalOffset, int keyboard, IEnumerable<int> selected) {
        if (ItemCount == 0) {
            return null;
        }

        double offset = Clamp(verticalOffset);
        if (Shows(keyboard, offset)) {
            return AnchorOf(keyboard, offset, users: true);
        }

        int first = -1;
        foreach (int index in selected) {
            if (Shows(index, offset) && (first < 0 || index < first)) {
                first = index;
            }
        }

        return first >= 0
            ? AnchorOf(first, offset, users: true)
            : AnchorOf(VisibleRange(offset).First, offset, users: false);
    }

    /// <summary>
    /// The offset at which <paramref name="anchor"/> stands where it stood;
    /// the user's cell that no longer fits there wholly is brought into view.
    /// </summary>
    public double Hold(TileAnchor anchor) {
        int index = Math.Clamp(anchor.Index, 0, Math.Max(0, ItemCount - 1));
        double offset = Clamp(CellAt(index).Y - anchor.Y);

        return anchor.KeepWhole ? OffsetToReveal(index, offset) : offset;
    }


    /// <summary>Any of the cell is on screen at the offset.</summary>
    private bool Shows(int index, double verticalOffset) {
        if (index < 0 || index >= ItemCount) {
            return false;
        }

        var cell = CellAt(index);

        return cell.Bottom > verticalOffset && cell.Y < verticalOffset + ViewportHeight;
    }

    private TileAnchor AnchorOf(int index, double verticalOffset, bool users) {
        var cell = CellAt(index);
        bool whole = cell.Y >= verticalOffset && cell.Bottom <= verticalOffset + ViewportHeight;

        return new TileAnchor(index, cell.Y - verticalOffset, users && whole);
    }
}
