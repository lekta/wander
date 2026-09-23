namespace Wander.Core.Navigation;

/// <summary>Who remembers a path, and follows a folder Wander moved or renamed (REDESIGN 4.12).</summary>
public enum PathHolder {
    /// <summary>The rows and open branches of both panels; the levels they stand in are read again.</summary>
    Panels,

    /// <summary>The bookmarks.</summary>
    Bookmarks,

    /// <summary>The views pinned to folders (FolderSettingsBook).</summary>
    FolderBook,

    /// <summary>The address bar's recent places.</summary>
    RecentPaths,

    /// <summary>The folder on screen as the listing knows it, where the user was in each folder, the pending intent (FolderSession).</summary>
    SelectionMemory,

    /// <summary>Paths cut or copied (decision B22).</summary>
    Clipboard,

    /// <summary>The history, and with it the folder open in the list.</summary>
    History,
}


/// <summary>
/// A folder Wander moved or renamed: every holder of a path told, in one
/// order, by one rule - the rewrite itself is each holder's own
/// (<c>PathRewrite.Under</c> underneath all of them). The order is the
/// point. The panels go first: re-pointing the open folder is a navigation,
/// and its place is looked for in a level being read again - the panels wait
/// for that answer rather than give up on a level that does not hold the
/// folder yet. The history goes last: its navigation lands the listing on
/// the new path, and everything it reads on the way - the view pinned to the
/// folder, where the user was in it - has to have moved already.
/// </summary>
public static class PathFollowing {
    /// <summary>The holders in the order they are told.</summary>
    public static IReadOnlyList<PathHolder> Order { get; } = new[] {
        PathHolder.Panels,
        PathHolder.Bookmarks,
        PathHolder.FolderBook,
        PathHolder.RecentPaths,
        PathHolder.SelectionMemory,
        PathHolder.Clipboard,
        PathHolder.History,
    };


    /// <summary>
    /// The rewrites <paramref name="moves"/> ask for: holder by holder in
    /// <see cref="Order"/>, each move in turn. A move onto itself asks for
    /// nothing.
    /// </summary>
    public static IReadOnlyList<(PathHolder Holder, string From, string To)> Plan(IReadOnlyList<(string From, string To)> moves) {
        var real = moves
            .Where(m => !string.Equals(Trim(m.From), Trim(m.To), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var steps = new List<(PathHolder, string, string)>(real.Count * Order.Count);
        foreach (var holder in Order) {
            foreach (var (from, to) in real) {
                steps.Add((holder, from, to));
            }
        }

        return steps;
    }


    private static string Trim(string path) {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
