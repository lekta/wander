using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// Where F3 goes past the last match in the pane (PLAN B6): on to the next
/// file a search inside files found, in the order the list shows them - the
/// row an arrow key would come to, less the rows found by their name alone,
/// which have no match in their text to show.
/// </summary>
public static class FindWalk {
    /// <summary>
    /// The found row after <paramref name="shownPath"/> - the first found
    /// row when the file on show is not in the list; null past the last.
    /// </summary>
    public static FileSystemEntry? Next(IReadOnlyList<FileSystemEntry> rows, string? shownPath) {
        int from = -1;
        if (shownPath is not null) {
            for (int i = 0; i < rows.Count; i++) {
                if (string.Equals(rows[i].FullPath, shownPath, StringComparison.OrdinalIgnoreCase)) {
                    from = i;

                    break;
                }
            }
        }

        for (int i = from + 1; i < rows.Count; i++) {
            if (rows[i] is { Kind: EntryKind.File, MatchSnippet: not null } row) {
                return row;
            }
        }

        return null;
    }
}
