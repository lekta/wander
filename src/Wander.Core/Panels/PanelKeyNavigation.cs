namespace Wander.Core.Panels;

/// <summary>The keys a panel answers with its cursor (decision B26).</summary>
public enum PanelKey {
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
}


/// <summary>What a key does in a panel.</summary>
public enum PanelKeyOutcome {
    /// <summary>Nothing: the edge, or a key with nothing to act on.</summary>
    None,

    /// <summary>The cursor goes to <see cref="PanelKeyResult.Path"/>.</summary>
    MoveCaret,

    /// <summary>The row <see cref="PanelKeyResult.Path"/> opens.</summary>
    Expand,

    /// <summary>The row <see cref="PanelKeyResult.Path"/> closes.</summary>
    Collapse,
}


/// <summary>The answer to a key: what happens, and to which row.</summary>
public readonly record struct PanelKeyResult(PanelKeyOutcome Outcome, string? Path) {
    public static readonly PanelKeyResult None = new(PanelKeyOutcome.None, null);
}


/// <summary>
/// The keys of a panel drawn as a list (decision P3), the way the tree of
/// Windows answers them: up and down to the neighbour line, Left closes an
/// open row with a chevron and otherwise goes to the row above it, Right
/// opens a closed one and otherwise goes to its first child, Home and End,
/// a page at a time. Arithmetic over the lines on screen, where the
/// off-by-one lives - so it lives here, where a test reaches it (like
/// <c>GridNavigation</c>).
/// </summary>
public static class PanelKeyNavigation {
    /// <param name="lines">The panel's lines as drawn.</param>
    /// <param name="caret">The row under the cursor; may be inside a closed branch, or null.</param>
    /// <param name="key">The key.</param>
    /// <param name="pageSize">How many lines the panel shows - a fact of the view.</param>
    public static PanelKeyResult Press(IReadOnlyList<VisibleRow> lines, string? caret, PanelKey key, int pageSize) {
        if (lines.Count == 0) {
            return PanelKeyResult.None;
        }

        int index = PanelView.IndexOf(lines, caret);
        if (index < 0) {
            int standIn = PanelView.NearestIndexOf(lines, caret);
            if (standIn >= 0) {
                // A cursor hidden by a branch closed over it: the arrows go
                // on from the row it is hidden under, and Left / Right put
                // the cursor on that row first.
                if (key is PanelKey.Left or PanelKey.Right) {
                    return Move(lines, standIn);
                }
                index = standIn;
            } else {
                // No cursor in this panel: the first key enters it - from
                // the top going down, from the bottom going up.
                return key is PanelKey.Up or PanelKey.End or PanelKey.PageUp
                    ? Move(lines, lines.Count - 1)
                    : Move(lines, 0);
            }
        }

        int step = Math.Max(1, pageSize - 1);
        var line = lines[index];

        return key switch {
            PanelKey.Up => index > 0 ? Move(lines, index - 1) : PanelKeyResult.None,
            PanelKey.Down => index < lines.Count - 1 ? Move(lines, index + 1) : PanelKeyResult.None,
            PanelKey.Home => index > 0 ? Move(lines, 0) : PanelKeyResult.None,
            PanelKey.End => index < lines.Count - 1 ? Move(lines, lines.Count - 1) : PanelKeyResult.None,
            PanelKey.PageUp => index > 0 ? Move(lines, Math.Max(0, index - step)) : PanelKeyResult.None,
            PanelKey.PageDown => index < lines.Count - 1 ? Move(lines, Math.Min(lines.Count - 1, index + step)) : PanelKeyResult.None,
            PanelKey.Left => Left(lines, index, line),
            PanelKey.Right => Right(lines, index, line),
            _ => PanelKeyResult.None,
        };
    }


    private static PanelKeyResult Left(IReadOnlyList<VisibleRow> lines, int index, VisibleRow line) {
        // An open row with no subfolders has nothing to close: Left goes up
        // at once, and the row stays open for a subfolder to come back to.
        if (line.IsExpanded && line.Row.HasChevron) {
            return new PanelKeyResult(PanelKeyOutcome.Collapse, line.Path);
        }

        for (int i = index - 1; i >= 0; i--) {
            if (lines[i].Depth < line.Depth) {
                return Move(lines, i);
            }
        }

        return PanelKeyResult.None;
    }

    private static PanelKeyResult Right(IReadOnlyList<VisibleRow> lines, int index, VisibleRow line) {
        if (!line.IsExpanded) {
            return line.Row.HasChevron
                ? new PanelKeyResult(PanelKeyOutcome.Expand, line.Path)
                : PanelKeyResult.None;
        }

        // Open: onto its first child - when its level has come in.
        return index + 1 < lines.Count && lines[index + 1].Depth == line.Depth + 1
            ? Move(lines, index + 1)
            : PanelKeyResult.None;
    }

    private static PanelKeyResult Move(IReadOnlyList<VisibleRow> lines, int index) {
        return new PanelKeyResult(PanelKeyOutcome.MoveCaret, lines[index].Path);
    }
}
