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

    /// <summary>
    /// How many lines the name under a large icon may wrap to before it is
    /// cut. Three, like Explorer: two is enough for a photograph out of a
    /// camera and not enough for anything a human named, and a name cut at
    /// «Снимок экрана 2026-05-28…» is exactly the case where the rest of it
    /// was the useful part.
    /// </summary>
    private const int LabelLines = 3;

    /// <summary>
    /// How many lines the caption under a gallery cell may use. One: the
    /// picture is the content there, and a three-line caption under every
    /// photograph turns a wall of photographs into a wall of file names.
    /// A name that does not fit is trimmed, and the full one is in the
    /// tooltip and in the preview pane.
    /// </summary>
    private const int GalleryLabelLines = 1;

    // Tiles: the width, the icon and the name's font size come from the
    // settings; the rest is the shape of the template itself and stays here,
    // where the box and the panel read one number.
    private const double TilesPadding = 6;
    private const double TilesMargin = 4;

    private TileMetrics(
        double contentWidth, double contentHeight, double margin,
        double imageSize, double labelHeight, double labelFontSize,
        double secondaryFontSize) {
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        Margin = margin;
        ImageSize = imageSize;
        LabelHeight = labelHeight;
        LabelFontSize = labelFontSize;
        SecondaryFontSize = secondaryFontSize;
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

    /// <summary>
    /// Font size of the second line under the name — the file's kind in the
    /// Tiles template. Equal to <see cref="LabelFontSize"/> in the modes that
    /// have no second line.
    /// </summary>
    public double SecondaryFontSize { get; }

    /// <summary>What the panel lays out on — the box plus its margin.</summary>
    public double CellWidth => ContentWidth + (2 * Margin);

    public double CellHeight => ContentHeight + (2 * Margin);


    /// <summary>The LargeIcons grid, from the four knobs the settings dialog offers.</summary>
    public static TileMetrics ForLargeIcons(double cellWidth, double imageSize, double margin, double labelFontSize) {
        double label = LabelBox(labelFontSize, LabelLines);

        return new TileMetrics(
            contentWidth: cellWidth,
            contentHeight: imageSize + (2 * ImageGap) + label,
            margin: margin,
            imageSize: imageSize,
            labelHeight: label,
            labelFontSize: labelFontSize,
            secondaryFontSize: labelFontSize);
    }


    /// <summary>
    /// The Tiles grid — a row of icon plus two lines of text, so its height
    /// is whichever of the two is taller plus the padding inside the tile.
    /// </summary>
    public static TileMetrics ForTiles(double cellWidth, double iconSize, double labelFontSize) {
        double label = TextLine(labelFontSize) + TextLine(TilesKindFontSize(labelFontSize));

        return new TileMetrics(
            contentWidth: cellWidth,
            contentHeight: Math.Max(iconSize, label) + (2 * TilesPadding),
            margin: TilesMargin,
            imageSize: iconSize,
            labelHeight: label,
            labelFontSize: labelFontSize,
            secondaryFontSize: TilesKindFontSize(labelFontSize));
    }


    /// <summary>
    /// The gallery grid. Same shape as LargeIcons — a picture with a
    /// caption under it — and a separate factory rather than a parameter on
    /// that one because the two views are sized for different jobs: a grid
    /// of icons you read the names of, and a grid of photographs you look
    /// at.
    /// </summary>
    public static TileMetrics ForGallery(double cellWidth, double imageSize, double margin, double labelFontSize) {
        double label = LabelBox(labelFontSize, GalleryLabelLines);

        return new TileMetrics(
            contentWidth: cellWidth,
            contentHeight: imageSize + (2 * ImageGap) + label,
            margin: margin,
            imageSize: imageSize,
            labelHeight: label,
            labelFontSize: labelFontSize,
            secondaryFontSize: labelFontSize);
    }


    private static double LabelBox(double fontSize, int lines) {
        return TextLine(fontSize) * lines;
    }

    private static double TextLine(double fontSize) {
        return Math.Ceiling(fontSize * LineSpacing);
    }


    /// <summary>
    /// The "kind" line under the name is a step smaller than the name, and
    /// never smaller than legible. Derived rather than settable: two font
    /// sizes for one tile is a knob nobody would turn.
    /// </summary>
    private static double TilesKindFontSize(double nameFontSize) {
        return Math.Max(8, nameFontSize - 1);
    }
}
