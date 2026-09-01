namespace Wander.Core.Layout;

/// <summary>One stop of the Tab ring - a whole area of the window.</summary>
public enum WindowZone {
    Toolbar,
    Address,
    Search,
    Bookmarks,
    Drives,
    FileList,
}


/// <summary>
/// Where the keyboard goes when it is moved by a shortcut rather than by a
/// click: the order Tab walks the window in, and which of the two folder
/// panels Ctrl+1 opens.
///
/// <para>
/// Tab moves a zone at a time rather than a control at a time - one press
/// lands on the toolbar, arrows pick the button. Which element inside a
/// zone can actually take the keyboard is the window's business and stays
/// there; the ring and the panel policy are arithmetic and a ladder of
/// fallbacks, which is where the off-by-one and the "and if neither?" case
/// hide, so they live here where a test reaches them.
/// </para>
/// </summary>
public static class WindowZones {
    /// <summary>
    /// Reading order: the top strip left to right, then the left pane top
    /// to bottom, then the list. The preview pane is deliberately absent -
    /// it has no keyboard behaviour yet, so a stop there would be a dead
    /// end (see BACKLOG, "клавиатура в панели просмотра").
    /// </summary>
    private static readonly WindowZone[] _order = {
        WindowZone.Toolbar,
        WindowZone.Address,
        WindowZone.Search,
        WindowZone.Bookmarks,
        WindowZone.Drives,
        WindowZone.FileList,
    };


    public static IReadOnlyList<WindowZone> Order => _order;


    /// <summary>
    /// The zones one Tab press should try, in the order it should try them:
    /// the neighbour first, then its neighbour, and so on the whole way
    /// round back to <paramref name="from"/>. A zone can refuse - collapsed
    /// bookmarks are not on screen, and all three toolbar buttons are
    /// disabled on a fresh start - so the caller walks this until one takes
    /// the keyboard.
    /// </summary>
    /// <param name="from">The zone the keyboard is in now.</param>
    /// <param name="delta"><c>+1</c> for Tab, <c>-1</c> for Shift+Tab.</param>
    public static IEnumerable<WindowZone> Ring(WindowZone from, int delta) {
        int start = Array.IndexOf(_order, from);
        int count = _order.Length;
        for (int step = 1; step <= count; step++) {
            yield return _order[(((start + (delta * step)) % count) + count) % count];
        }
    }


    /// <summary>
    /// Which folder panel Ctrl+1 and Ctrl+Shift+E open. Both expand a panel
    /// down to the folder on screen, so the answer is also what tells the
    /// user where they are.
    /// </summary>
    /// <param name="toggle">
    /// What separates the two shortcuts. Ctrl+1 (<c>true</c>) pressed while
    /// already in a panel swaps to the other one, which is the whole point
    /// of one key for two panels; Ctrl+Shift+E (<c>false</c>) always lands
    /// in the panel the folder was opened from.
    /// </param>
    /// <param name="focused">The zone the keyboard is in, or null for anywhere else.</param>
    /// <param name="owner">
    /// The panel the current folder was opened from, or null when it was
    /// opened from neither - the address bar, a double click, a restored
    /// session.
    /// </param>
    /// <param name="last">The panel the keyboard was in last, for that "neither" case.</param>
    /// <param name="hasBookmarks">
    /// False when every default bookmark is switched off and none added: an
    /// empty panel is not somewhere to send the keyboard.
    /// </param>
    public static WindowZone FolderPane(
        bool toggle, WindowZone? focused, WindowZone? owner, WindowZone last, bool hasBookmarks) {
        var target = owner ?? WindowZone.Drives;
        if (toggle) {
            target = focused switch {
                WindowZone.Bookmarks => WindowZone.Drives,
                WindowZone.Drives => WindowZone.Bookmarks,
                _ => owner ?? last,
            };
        }

        return target == WindowZone.Bookmarks && !hasBookmarks ? WindowZone.Drives : target;
    }
}
