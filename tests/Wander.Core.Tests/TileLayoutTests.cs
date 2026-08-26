using Wander.Core.Layout;

namespace Wander.Core.Tests;

public class TileLayoutTests {
    /// <summary>
    /// A pane six cells wide with a hundred items — the shape the tile views
    /// actually run in, reused so the numbers below stay comparable.
    /// </summary>
    private static TileLayout Grid(int items = 100, double viewportWidth = 700, double viewportHeight = 400) {
        return new TileLayout(viewportWidth, viewportHeight, cellWidth: 114, cellHeight: 115, itemCount: items);
    }


    // --- Columns -------------------------------------------------------

    [Theory]
    [InlineData(700, 6)]
    [InlineData(684, 6)]   // exactly six cells
    [InlineData(683, 5)]   // one unit short of six
    [InlineData(114, 1)]
    public void Columns_IsHowManyWholeCellsFit(double viewportWidth, int expected) {
        Assert.Equal(expected, Grid(viewportWidth: viewportWidth).Columns);
    }


    /// <summary>
    /// A pane narrower than one cell still has to lay out — clipped, but
    /// laid out. Zero columns would divide by zero everywhere below.
    /// </summary>
    [Theory]
    [InlineData(50)]
    [InlineData(0)]
    [InlineData(-10)]
    public void Columns_IsNeverZero(double viewportWidth) {
        Assert.Equal(1, Grid(viewportWidth: viewportWidth).Columns);
    }


    /// <summary>
    /// The bug the tile views actually had: the column count came from one
    /// cell size while the cells were placed at another, so the grid was
    /// wider than the pane and the last column was cut off. Columns and
    /// cell size are now read from the same value, and this is the
    /// invariant that says so.
    /// </summary>
    [Theory]
    [InlineData(700, 114)]
    [InlineData(1259, 114)]
    [InlineData(731, 260)]
    [InlineData(200, 300)]
    public void ExtentWidth_NeverExceedsTheViewport(double viewportWidth, double cellWidth) {
        var layout = new TileLayout(viewportWidth, 400, cellWidth, 115, itemCount: 50);

        // The single-column case is the exception: one cell that does not
        // fit is still shown, clipped.
        if (layout.Columns > 1) {
            Assert.True(layout.ExtentWidth <= viewportWidth,
                $"extent {layout.ExtentWidth} > viewport {viewportWidth}");
        }
    }


    // --- Rows and extent ------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    [InlineData(12, 2)]
    [InlineData(13, 3)]
    public void Rows_CountsThePartialLastRow(int items, int expectedRows) {
        var layout = Grid(items);

        Assert.Equal(expectedRows, layout.Rows);
        Assert.Equal(expectedRows * 115, layout.ExtentHeight);
    }


    [Fact]
    public void EmptyList_HasNoExtentAndNothingToScroll() {
        var layout = Grid(items: 0);

        Assert.Equal(0, layout.ExtentHeight);
        Assert.Equal(0, layout.MaxVerticalOffset);
        Assert.Equal((0, -1), layout.VisibleRange(0));
    }


    [Fact]
    public void ContentShorterThanTheViewport_DoesNotScroll() {
        Assert.Equal(0, Grid(items: 6).MaxVerticalOffset);
    }


    // --- Cell positions -------------------------------------------------

    [Fact]
    public void CellAt_WalksLeftToRightThenDown() {
        var layout = Grid();

        Assert.Equal(new TileRect(0, 0, 114, 115), layout.CellAt(0));
        Assert.Equal(new TileRect(114, 0, 114, 115), layout.CellAt(1));
        Assert.Equal(new TileRect(5 * 114, 0, 114, 115), layout.CellAt(5));
        Assert.Equal(new TileRect(0, 115, 114, 115), layout.CellAt(6));
        Assert.Equal(new TileRect(114, 115, 114, 115), layout.CellAt(7));
    }


    // --- Visible range ---------------------------------------------------

    /// <summary>
    /// The range has to cover the viewport, or the list shows fewer files
    /// than fit — one of the two symptoms the tile views had.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(115)]
    [InlineData(1000)]
    [InlineData(4000)]
    public void VisibleRange_CoversEveryCellTouchingTheViewport(double offset) {
        var layout = Grid();
        double clamped = layout.Clamp(offset);
        var (first, last) = layout.VisibleRange(clamped);

        for (int i = 0; i < layout.ItemCount; i++) {
            var cell = layout.CellAt(i);
            bool onScreen = cell.Bottom > clamped && cell.Y < clamped + layout.ViewportHeight;
            if (onScreen) {
                Assert.True(i >= first && i <= last, $"item {i} is on screen but outside {first}..{last}");
            }
        }
    }


    [Fact]
    public void VisibleRange_StartsAtTheRowTheOffsetLandsIn() {
        var layout = Grid();

        Assert.Equal(0, layout.VisibleRange(0).First);
        Assert.Equal(0, layout.VisibleRange(114).First);      // still inside row 0
        Assert.Equal(6, layout.VisibleRange(115).First);      // row 1
        Assert.Equal(12, layout.VisibleRange(230).First);     // row 2
    }


    [Fact]
    public void VisibleRange_StopsAtTheLastItem() {
        var layout = Grid(items: 20);
        var (_, last) = layout.VisibleRange(layout.MaxVerticalOffset);

        Assert.Equal(19, last);
    }


    /// <summary>
    /// One row past the bottom edge, so a wheel notch does not expose a band
    /// of empty cells before the next layout pass catches up.
    /// </summary>
    [Fact]
    public void VisibleRange_KeepsOneRowOfSlackBelowTheFold() {
        var layout = Grid();
        var (first, last) = layout.VisibleRange(0);

        // 400 / 115 → 4 rows on screen, plus one of slack, times 6 columns.
        Assert.Equal(0, first);
        Assert.Equal((4 + 1) * 6 - 1, last);
    }


    /// <summary>
    /// A list that shrank under a scrolled view — deleting the tail of a big
    /// folder — must not ask for containers past the end.
    /// </summary>
    [Fact]
    public void VisibleRange_SurvivesAnOffsetPastTheEnd() {
        var layout = Grid(items: 7);
        var (first, last) = layout.VisibleRange(100000);

        Assert.InRange(first, 0, 6);
        Assert.InRange(last, first, 6);
    }


    // --- Offsets ---------------------------------------------------------

    [Fact]
    public void Clamp_KeepsTheViewInsideTheContent() {
        var layout = Grid();

        Assert.Equal(0, layout.Clamp(-500));
        Assert.Equal(layout.MaxVerticalOffset, layout.Clamp(double.MaxValue));
        Assert.Equal(500, layout.Clamp(500));
    }


    [Fact]
    public void OffsetToReveal_LeavesAVisibleCellAlone() {
        var layout = Grid();

        Assert.Equal(0, layout.OffsetToReveal(0, 0));
        Assert.Equal(0, layout.OffsetToReveal(5, 0));
    }


    [Fact]
    public void OffsetToReveal_ScrollsTheShortestWayToACellOffScreen() {
        var layout = Grid();

        // Item 60 sits in row 10, at y = 1150. Bringing it in from the top
        // means aligning its bottom with the bottom of the viewport.
        Assert.Equal(1150 + 115 - 400, layout.OffsetToReveal(60, 0));

        // Coming back up, its top aligns with the top of the viewport.
        Assert.Equal(1150, layout.OffsetToReveal(60, 3000));
    }


    [Fact]
    public void OffsetToReveal_StaysInsideTheContent() {
        var layout = Grid(items: 8);

        Assert.InRange(layout.OffsetToReveal(7, 0), 0, layout.MaxVerticalOffset);
    }


    // --- Degenerate input ------------------------------------------------

    /// <summary>
    /// A cell size of zero is what the panel starts with before a container
    /// has ever been measured. It must produce a finite layout rather than
    /// an infinite column count or a division by zero.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, -5)]
    public void DegenerateCellSize_StillProducesAFiniteLayout(double cellWidth, double cellHeight) {
        var layout = new TileLayout(700, 400, cellWidth, cellHeight, itemCount: 10);

        Assert.True(layout.Columns >= 1);
        Assert.True(double.IsFinite(layout.ExtentHeight));
        var (first, last) = layout.VisibleRange(0);
        Assert.InRange(first, 0, 9);
        Assert.InRange(last, first, 9);
    }


    [Fact]
    public void NegativeItemCount_IsTreatedAsEmpty() {
        var layout = new TileLayout(700, 400, 114, 115, itemCount: -3);

        Assert.Equal(0, layout.ItemCount);
        Assert.Equal((0, -1), layout.VisibleRange(0));
    }
}
