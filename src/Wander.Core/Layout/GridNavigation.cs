namespace Wander.Core.Layout;

/// <summary>One arrow-key press in a wrap layout.</summary>
public enum GridStep { Left, Right, Up, Down }


/// <summary>
/// Where the caret lands when an arrow key is pressed at the edge of a wrap
/// layout — the cases WPF's directional navigation answers with "nowhere",
/// because there is no container in that direction.
///
/// <para>
/// The model is that a wrap layout is a linear list folded into rows:
/// Left/Right walk that list and therefore cross row boundaries, while
/// Up/Down move by a whole row. Two ends need a rule of their own: Up from
/// the top row goes to the very first item and Down from the bottom row to
/// the very last one (what Home and End do), and Down from a full row into a
/// short last row lands on its last item rather than falling off the grid.
/// </para>
///
/// <para>
/// Pure arithmetic, so it lives here rather than in the panel: the edges are
/// exactly where an off-by-one hides, and none of it needs a window on
/// screen to check. The caller (<c>Wander.App/Views/FileListView</c>) asks
/// only for the steps WPF leaves undone.
/// </para>
/// </summary>
public static class GridNavigation {
    /// <summary>
    /// The index the selection moves to, or <c>-1</c> when the press has
    /// nowhere to go and should be left alone.
    /// </summary>
    /// <param name="index">Where the selection is now.</param>
    /// <param name="step">Which arrow was pressed.</param>
    /// <param name="columns">Cells per row — see <see cref="TileLayout.Columns"/>.</param>
    /// <param name="count">How many items the list holds.</param>
    public static int Move(int index, GridStep step, int columns, int count) {
        if (count <= 0 || index < 0 || index >= count || columns <= 0) {
            return -1;
        }

        switch (step) {
            // Left and Right walk the folded list, so the end of a row leads
            // into the next one. The two outer ends stay put: wrapping the
            // last item round to the first would be a jump, not a step.
            case GridStep.Left:
                return index > 0 ? index - 1 : -1;

            case GridStep.Right:
                return index < count - 1 ? index + 1 : -1;

            case GridStep.Up:
                if (index >= columns) {
                    return index - columns;
                }

                return index > 0 ? 0 : -1;

            case GridStep.Down:
                int below = index + columns;
                if (below < count) {
                    return below;
                }

                // Nothing directly below: either this is the bottom row, or
                // the last row is short and stops before this column. Both
                // mean the last item is what the user is reaching for.
                return index < count - 1 ? count - 1 : -1;

            default:
                return -1;
        }
    }


    /// <summary>
    /// Is this the press WPF cannot answer? Anything in the middle of the
    /// grid has a neighbour in that direction and is the control's own
    /// business; only the row ends and the outer rows come here.
    /// </summary>
    public static bool IsAtEdge(int index, GridStep step, int columns, int count) {
        if (index < 0 || columns <= 0) {
            return false;
        }

        return step switch {
            GridStep.Left => index % columns == 0,
            GridStep.Right => index % columns == columns - 1 || index == count - 1,
            GridStep.Up => index < columns,
            GridStep.Down => index + columns >= count,
            _ => false,
        };
    }


    /// <summary>
    /// The item a Shift-extension grows from. What Shift builds is one run,
    /// so the anchor is the end of it the caret is *not* sitting on; with
    /// nothing selected the caret is both ends at once.
    /// </summary>
    /// <param name="caret">Where the caret is now.</param>
    /// <param name="count">How many items the list holds.</param>
    /// <param name="isSelected">Whether the item at an index is selected.</param>
    public static int Anchor(int caret, int count, Func<int, bool> isSelected) {
        int first = -1;
        int last = -1;
        for (int i = 0; i < count; i++) {
            if (isSelected(i)) {
                if (first < 0) {
                    first = i;
                }
                last = i;
            }
        }

        if (first < 0) {
            return caret;
        }

        return caret == last ? first : last;
    }
}
