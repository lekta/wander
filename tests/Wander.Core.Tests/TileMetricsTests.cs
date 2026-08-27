using Wander.Core.Layout;
using Wander.Core.Persistence;

namespace Wander.Core.Tests;

/// <summary>
/// What these guard is one property: the cell is big enough to hold what is
/// drawn in it, and it depends on nothing but the settings. Every tile bug so
/// far came from the other direction — the cell learning its size from a
/// container, and so from whichever file happened to be on screen.
/// </summary>
public class TileMetricsTests {
    private static TileMetrics Icons(int cellWidth = 100, int imageSize = 72, int margin = 2, int fontSize = 12) {
        return TileMetrics.ForLargeIcons(cellWidth, imageSize, margin, fontSize);
    }

    private static TileMetrics Tiles(int cellWidth = 220, int iconSize = 32, int fontSize = 12) {
        return TileMetrics.ForTiles(cellWidth, iconSize, fontSize);
    }


    [Fact]
    public void Cell_IsTheContentPlusItsMarginOnBothSides() {
        var metrics = Icons(cellWidth: 100, margin: 2);

        Assert.Equal(104, metrics.CellWidth);
        Assert.Equal(metrics.ContentHeight + 4, metrics.CellHeight);
    }


    /// <summary>
    /// The tile is drawn at ContentWidth x ContentHeight inside a cell — if
    /// the cell were the smaller of the two, neighbouring tiles would overlap.
    /// </summary>
    [Theory]
    [InlineData(60, 24, 0, 8)]
    [InlineData(100, 72, 2, 12)]
    [InlineData(320, 256, 32, 24)]
    public void Cell_AlwaysHoldsItsContent(int cellWidth, int imageSize, int margin, int fontSize) {
        var metrics = Icons(cellWidth, imageSize, margin, fontSize);

        Assert.True(metrics.CellWidth >= metrics.ContentWidth);
        Assert.True(metrics.CellHeight >= metrics.ContentHeight);
    }


    /// <summary>
    /// The icon, the air around it and the wrapped label all have to fit, or
    /// the label is clipped — which is what a hard-coded 32 px label box did
    /// to the largest font the settings dialog offers.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(24)]
    public void Content_HoldsTheIconAndItsLabelLines(int fontSize) {
        var metrics = Icons(imageSize: 72, fontSize: fontSize);

        Assert.True(metrics.LabelHeight >= 3 * fontSize);
        Assert.Equal(72 + (2 * TileMetrics.ImageGap) + metrics.LabelHeight, metrics.ContentHeight);
    }


    [Fact]
    public void LargeIcons_TakesItsSizesFromTheSettings() {
        var metrics = Icons(cellWidth: 140, imageSize: 96, margin: 6, fontSize: 14);

        Assert.Equal(140, metrics.ContentWidth);
        Assert.Equal(96, metrics.ImageSize);
        Assert.Equal(6, metrics.Margin);
        Assert.Equal(14, metrics.LabelFontSize);
    }


    /// <summary>
    /// A bigger icon or a bigger font may only ever grow the cell. Anything
    /// else means a setting that shrinks the grid while the tiles stay put.
    /// </summary>
    [Fact]
    public void BiggerSettings_NeverGiveASmallerCell() {
        var small = Icons(cellWidth: 100, imageSize: 72, margin: 2, fontSize: 12);
        var large = Icons(cellWidth: 100, imageSize: 96, margin: 2, fontSize: 16);

        Assert.True(large.CellHeight > small.CellHeight);
        Assert.Equal(small.CellWidth, large.CellWidth);
    }


    /// <summary>
    /// The shipped sizes are Explorer's, and that is a decision worth
    /// pinning: they are easy to nudge by accident (a settings default is
    /// one number in a record) and hard to notice afterwards — the grid just
    /// slowly stops looking like the thing it is meant to replace.
    /// </summary>
    [Fact]
    public void DefaultLargeIcons_KeepExplorersProportions() {
        var settings = new AppSettings();
        var metrics = TileMetrics.ForLargeIcons(
            settings.LargeIconCellWidth, settings.LargeIconImageSize,
            settings.LargeIconMargin, settings.LargeIconLabelFontSize);

        Assert.Equal(96, metrics.ImageSize);
        // 36 px of air around the picture.
        Assert.Equal(36, metrics.ContentWidth - metrics.ImageSize);
        // Three lines of name under it, at 17 px a line.
        Assert.Equal(51, metrics.LabelHeight);
    }


    /// <summary>
    /// Tiles are a row: the icon on the left, two lines of text on the right.
    /// So the box has to clear whichever of the two is taller.
    /// </summary>
    [Fact]
    public void Tiles_ClearTheIconAndTheTwoTextLines() {
        var metrics = Tiles();

        Assert.True(metrics.ContentHeight > metrics.ImageSize);
        Assert.True(metrics.ContentHeight > metrics.LabelHeight);
        Assert.True(metrics.CellWidth > metrics.ContentWidth);
    }

    /// <summary>
    /// A tile big enough for a 96-px icon has to be taller than one built
    /// for a 32-px one — the same "settings drive the geometry" property the
    /// icon grid has, now that Tiles has its own settings too.
    /// </summary>
    [Fact]
    public void Tiles_GrowWithTheirIcon() {
        var small = Tiles(iconSize: 32);
        var large = Tiles(iconSize: 96);

        Assert.True(large.CellHeight > small.CellHeight);
        Assert.Equal(small.CellWidth, large.CellWidth);
    }

    /// <summary>
    /// The second line is derived from the name's size, never independent of
    /// it, and never shrinks below legible.
    /// </summary>
    [Fact]
    public void Tiles_SecondLineFollowsTheNameSize() {
        Assert.True(Tiles(fontSize: 16).SecondaryFontSize < 16);
        Assert.True(Tiles(fontSize: 8).SecondaryFontSize >= 8);
    }


    /// <summary>
    /// Same settings in, same numbers out — the whole point of moving the
    /// cell size out of the measured container and into arithmetic.
    /// </summary>
    [Fact]
    public void SameSettings_GiveTheSameCell() {
        Assert.Equal(Icons(), Icons());
        Assert.Equal(Tiles(), Tiles());
    }
}
