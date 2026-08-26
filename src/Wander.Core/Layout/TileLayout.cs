namespace Wander.Core.Layout;

/// <summary>
/// One cell of a <see cref="TileLayout"/>, in layout units.
/// </summary>
public readonly record struct TileRect(double X, double Y, double Width, double Height) {
    public double Bottom => Y + Height;
}


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
}
