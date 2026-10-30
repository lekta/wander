namespace Wander.Core.Layout;

/// <summary>
/// What the user made of the table's columns, kept between sessions. Out of
/// the box "Имя" takes what the others leave, so the table fits the window and
/// has no scroll bar; the first column resized by hand ends that, and from
/// then on every width is where it was put, as in Explorer - a gap on the
/// right, a scroll bar only when the columns do not fit.
/// </summary>
public sealed record DetailsColumns {
    /// <summary>"Имя" does not get narrower than this by itself: below it, the scroll bar.</summary>
    public const double NameMinWidth = 160;

    /// <summary>
    /// A row is this much wider than its cells: its frame, 1 px a side, always
    /// there and usually invisible (the caret's). Forgotten, a table exactly as
    /// wide as the list had a horizontal scroll bar for those two pixels.
    /// </summary>
    public const double RowFrameWidth = 2;

    /// <summary>"Имя" takes what the others leave; false once a column has been resized by hand.</summary>
    public bool NameFills { get; init; } = true;

    /// <summary>Widths by column; "Имя" among them only when it does not fill.</summary>
    public IReadOnlyDictionary<string, double> Widths { get; init; } = new Dictionary<string, double>();

    /// <summary>Columns in the order they were dragged to; empty for the order out of the box.</summary>
    public IReadOnlyList<string> Order { get; init; } = Array.Empty<string>();

    /// <summary>The columns switched off in the header's menu.</summary>
    public IReadOnlyList<string> Hidden { get; init; } = Array.Empty<string>();


    /// <summary>How wide "Имя" is while it fills: all a list this wide has left beside columns this wide.</summary>
    public static double FillWidth(double viewport, double others) {
        return Math.Max(NameMinWidth, viewport - others - RowFrameWidth);
    }
}
