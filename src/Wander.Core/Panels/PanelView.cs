using System.Collections.Immutable;

namespace Wander.Core.Panels;

/// <summary>
/// One line of a panel as it is drawn: the row, how deep it sits, whether it
/// is open. The panel is drawn as a plain list of these (decision P3) - the
/// indent is the depth, the tree is only in what this list is made from.
/// </summary>
/// <param name="Key">
/// The line's identity from one drawing to the next: the chain of paths down
/// to it, the top row's position included - the same folder can be a line
/// twice (a bookmark inside another one), and a list needs its items told
/// apart.
/// </param>
/// <param name="Row">The row.</param>
/// <param name="Depth">0 for a top row.</param>
/// <param name="IsExpanded">Open: its level is drawn under it.</param>
/// <param name="Parent">The row it is under, or null for a top row.</param>
public sealed record VisibleRow(string Key, PanelRow Row, int Depth, bool IsExpanded, string? Parent) {
    public string Path => Row.Path;
}


/// <summary>What a panel shows, worked out from its state.</summary>
public static class PanelView {
    /// <summary>
    /// The lines of <paramref name="panel"/>, top to bottom: every top row,
    /// and under each open row the rows of its level, as far down as rows
    /// are open. A row open with no rows read yet has nothing under it.
    /// </summary>
    public static ImmutableArray<VisibleRow> Rows(PanelState panel) {
        var lines = ImmutableArray.CreateBuilder<VisibleRow>();
        var top = panel.Top;
        for (int i = 0; i < top.Length; i++) {
            Add(panel, lines, top[i], i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + PanelPaths.Key(top[i].Path), 0, null);
        }

        return lines.ToImmutable();
    }

    /// <summary>The first line on <paramref name="path"/>, or -1.</summary>
    public static int IndexOf(IReadOnlyList<VisibleRow> lines, string? path) {
        if (path is null) {
            return -1;
        }

        for (int i = 0; i < lines.Count; i++) {
            if (PanelPaths.Same(lines[i].Path, path)) {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The line standing in for <paramref name="path"/>: its own when it is
    /// drawn, else the nearest row above it that is - a cursor left inside a
    /// branch that was closed over it. -1 when nothing of it is on screen.
    /// </summary>
    public static int NearestIndexOf(IReadOnlyList<VisibleRow> lines, string? path) {
        for (string? step = path; step is not null; step = PanelPaths.Parent(step)) {
            int index = IndexOf(lines, step);
            if (index >= 0) {
                return index;
            }
        }

        return -1;
    }


    private static void Add(PanelState panel, ImmutableArray<VisibleRow>.Builder lines, PanelRow row, string key, int depth, string? parent) {
        // Open is the user's word, not the folder's: a row that lost its last
        // subfolder stays open - there is no chevron to show it - and one
        // coming back shows under it at once.
        bool open = !row.IsLeaf && panel.IsExpanded(row.Path);
        lines.Add(new VisibleRow(key, row, depth, open, parent));
        if (!open) {
            return;
        }

        foreach (var child in panel.LevelOf(row.Path).Rows) {
            Add(panel, lines, child, key + ">" + PanelPaths.Key(child.Path), depth + 1, row.Path);
        }
    }
}
