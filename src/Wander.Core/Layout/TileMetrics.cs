namespace Wander.Core.Layout;

/// <summary>
/// The fixed geometry of one tile view: how big a cell is, and how big the
/// thing drawn inside it is.
///
/// <para>
/// The point of this type is that the cell size is an <b>input</b> to
/// layout. The panel used to learn it by measuring a realised container,
/// which put the content inside the geometry: a cell was as big as whatever
/// container happened to be sampled, and a container is as big as its
/// bindings, its thumbnail and the text metrics of its label at that
/// instant. The trace that closed this had the LargeIcons grid running on
/// 70x40 cells (a container measured before its icon existed — the label and
/// nothing else) while the tiles it drew were 104x112; another session
/// latched a 2:3 cell, which is the aspect ratio of the photograph, not of
/// the template.
/// </para>
///
/// <para>
/// So: both templates draw at these numbers and the panel lays out on these
/// numbers. One source, computed from the settings, so the grid and what
/// sits in it cannot disagree — and nothing about a folder's contents can
/// move the grid.
/// </para>
/// </summary>
public readonly record struct TileMetrics {
    /// <summary>
    /// Air above and below the icon image, in layout units. Mirrored by the
    /// <c>Margin="0,2,0,2"</c> on the icon in the LargeIcons template — the
    /// two have to agree, or the label loses a couple of units to clipping.
    /// </summary>
    public const double ImageGap = 2;

    /// <summary>
    /// A line of text is this much taller than its font size. Segoe UI's
    /// default line spacing, rounded up: enough that a label of the given
    /// size fits its box, which is all this needs to be.
    /// </summary>
    private const double LineSpacing = 1.35;

    /// <summary>The name label is two lines in both tile modes.</summary>
    private const int LabelLines = 2;

    // Tiles: not user-tunable (yet — PLAN A1), but they live here rather
    // than in the template so that the box and the panel read one number.
    // The font sizes mirror the Tiles template's own: the name line uses
    // the default 12, the "kind" line under it 11.
    private const double TilesContentWidth = 220;
    private const double TilesPadding = 6;
    private const double TilesMargin = 4;
    private const double TilesIconSize = 32;
    private const double TilesNameFontSize = 12;
    private const double TilesKindFontSize = 11;


    private TileMetrics(
        double contentWidth, double contentHeight, double margin,
        double imageSize, double labelHeight, double labelFontSize) {
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        Margin = margin;
        ImageSize = imageSize;
        LabelHeight = labelHeight;
        LabelFontSize = labelFontSize;
    }


    /// <summary>Width of the template's own box, without the margin around it.</summary>
    public double ContentWidth { get; }

    /// <summary>Height of that box.</summary>
    public double ContentHeight { get; }

    /// <summary>Gap around the box — the space between two neighbouring tiles is twice this.</summary>
    public double Margin { get; }

    /// <summary>Side of the square the icon image is drawn in.</summary>
    public double ImageSize { get; }

    /// <summary>Height of the name label: two lines, clipped past that.</summary>
    public double LabelHeight { get; }

    public double LabelFontSize { get; }

    /// <summary>What the panel lays out on — the box plus its margin.</summary>
    public double CellWidth => ContentWidth + (2 * Margin);

    public double CellHeight => ContentHeight + (2 * Margin);


    /// <summary>The LargeIcons grid, from the four knobs the settings dialog offers.</summary>
    public static TileMetrics ForLargeIcons(double cellWidth, double imageSize, double margin, double labelFontSize) {
        double label = LabelBox(labelFontSize);

        return new TileMetrics(
            contentWidth: cellWidth,
            contentHeight: imageSize + (2 * ImageGap) + label,
            margin: margin,
            imageSize: imageSize,
            labelHeight: label,
            labelFontSize: labelFontSize);
    }


    /// <summary>
    /// The Tiles grid — a row of icon plus two lines of text, so its height
    /// is whichever of the two is taller plus the padding inside the tile.
    /// </summary>
    public static TileMetrics ForTiles() {
        double label = TextLine(TilesNameFontSize) + TextLine(TilesKindFontSize);

        return new TileMetrics(
            contentWidth: TilesContentWidth,
            contentHeight: Math.Max(TilesIconSize, label) + (2 * TilesPadding),
            margin: TilesMargin,
            imageSize: TilesIconSize,
            labelHeight: label,
            labelFontSize: TilesNameFontSize);
    }


    private static double LabelBox(double fontSize) {
        return TextLine(fontSize) * LabelLines;
    }

    private static double TextLine(double fontSize) {
        return Math.Ceiling(fontSize * LineSpacing);
    }
}
