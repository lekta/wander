using Wander.Core.Navigation;
using Wander.Core.Rename;

namespace Wander.Core.Persistence;

/// <summary>
/// Top-level snapshot persisted to <c>state.json</c>. Four logical buckets:
/// <see cref="Session"/> (where the user left off), <see cref="Favorites"/>
/// (their user-defined bookmark list), <see cref="Window"/> (window
/// placement), and <see cref="Settings"/> (preference toggles). Each
/// bucket is its own record so it can grow independently without thrashing
/// the top-level shape.
/// </summary>
public sealed record AppState {
    /// <summary>
    /// The shape of this file, as a number that goes up whenever a change
    /// here would be lost or misread by an older Wander (PLAN AD11,
    /// decision of 2026-09-22). Older builds still run and still write:
    /// what they must not do is write over a file of a newer shape, and
    /// this is what they compare against.
    ///
    /// <para>
    /// Raise it when a field's meaning changes or a block appears that an
    /// older build would drop on its next write; a field an older build
    /// simply ignores and carries through costs nothing and stays at the
    /// same number. The full rule - which copy is allowed to write when
    /// several versions of Wander live on one machine - is still open
    /// (PLAN AD11).
    /// </para>
    /// </summary>
    public const int CurrentVersion = 1;


    /// <summary>
    /// The shape this file was written in; see <see cref="CurrentVersion"/>.
    /// A file written before it existed has no such field and reads as the
    /// current shape - the default - not as a shape to keep away from.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Where the user left off — folder, expansions, panes, view mode.</summary>
    public SessionState Session { get; init; } = new();

    /// <summary>
    /// User-defined bookmark folders (full paths). Order is preserved as
    /// the user reorders them; special folders (This PC, Downloads) live
    /// separately and are toggled via <see cref="AppSettings"/>.
    /// </summary>
    public IReadOnlyList<string> Favorites { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Window position / size at close. Null on a fresh install (or after a
    /// rolled-back schema) so consumers fall back to the XAML defaults
    /// rather than to (0, 0, 0, 0). Width/Height are remembered even when
    /// the window was Maximized at close — that way restoring it to Normal
    /// lands at the same size it had before being maximized.
    /// </summary>
    public WindowGeometry? Window { get; init; }

    /// <summary>
    /// The conflict window's own placement. It is a working surface, not a
    /// message box — a list of pairs with thumbnails that the user resizes
    /// to see more of at once — and a window that forgets that every time
    /// is a window that gets resized every time. Null until it is opened
    /// once.
    /// </summary>
    public WindowGeometry? ConflictWindow { get; init; }

    /// <summary>
    /// What the batch-rename window was last set to. Where the user left
    /// off, not a preference: the same numbering pass is usually run over
    /// the next folder too. Null until the window has been used once.
    /// </summary>
    public RenameRules? RenameRules { get; init; }

    /// <summary>
    /// The last templates the batch-rename window applied, newest first
    /// (<see cref="RenameTemplateHistory"/>) - its template field's
    /// drop-down.
    /// </summary>
    public IReadOnlyList<string> RenameTemplates { get; init; } = Array.Empty<string>();

    /// <summary>
    /// User preferences (separate from session-state above). Always
    /// non-null so consumers don't have to null-check; the default
    /// record represents the out-of-the-box settings.
    /// </summary>
    public AppSettings Settings { get; init; } = new();

    /// <summary>
    /// Version of the build that last wrote this file - the three numbers
    /// and the suffix (<c>0.5.0-beta</c>), without the commit and without
    /// the build number of PLAN AH: a rebuild of the same version is the
    /// same version here.
    ///
    /// <para>
    /// Read for one thing: dropping the thumbnail cache after an update.
    /// Thumbnails are keyed by path and file stamps, not by the code that
    /// produced them, so a fix to decoding or sizing leaves every previously
    /// cached picture wrong and no key changes to notice. Regenerating a few
    /// hundred thumbnails once per update is cheap; a wrong thumbnail that
    /// never expires is a bug report.
    /// </para>
    ///
    /// <para>
    /// Empty on a fresh install and on files written before this field
    /// existed — both treated as "different build", which costs one extra
    /// regeneration and nothing else.
    /// </para>
    /// </summary>
    public string LastRunVersion { get; init; } = string.Empty;
}


/// <summary>
/// "Where the user left off" — the session-resume bucket. Distinct from
/// <see cref="AppSettings"/> (long-term preferences) and
/// <see cref="WindowGeometry"/> (chrome placement): everything in here is
/// resetable without surprising the user, and a fresh install starts with
/// the defaults below.
/// </summary>
public sealed record SessionState {
    /// <summary>
    /// Folder the user was on when the session closed, together with the
    /// panel context (drives / bookmarks / address / …) so the restored
    /// session re-expands the right tree. Null on a fresh install.
    /// </summary>
    public NavigationStop? LastPath { get; init; }

    /// <summary>
    /// Where the list stood in <see cref="LastPath"/>: the file it was on and
    /// the row first on screen (2026-09-25). Null on a file written before
    /// this field existed, and when there was nothing to remember.
    /// </summary>
    public ListPlace? LastPlace { get; init; }

    /// <summary>
    /// Legacy (up to 0.4.x): folders where the user picked a view by hand.
    /// Read once, on the first start after the update, and moved into
    /// <c>folders.json</c> (<c>IFolderSettingsStore</c>); never written
    /// again, so the next save leaves it empty. The "last view" that sat
    /// beside it is not carried over: the default view is a setting now
    /// (<c>AppSettings.DefaultViewMode</c>, decision 2026-09-23).
    /// </summary>
    public IReadOnlyList<FolderViewMode> ManualViewModes { get; init; } = Array.Empty<FolderViewMode>();

    /// <summary>
    /// Tree nodes the user had expanded at close, scoped per panel.
    /// The same path can live in both panels (e.g. user-favourite that
    /// also exists deep in the drives subtree) — each ownership is
    /// recorded separately, so restoring keeps both panels matching
    /// their last visible state independently.
    /// </summary>
    public IReadOnlyList<NavigationStop> ExpandedPaths { get; init; } = Array.Empty<NavigationStop>();

    /// <summary>
    /// Folders visited most recently, newest first — the address-bar
    /// dropdown. Capped by <c>RecentPaths.DefaultCapacity</c> on load, so
    /// a hand-grown list in state.json can't bloat the popup.
    /// </summary>
    public IReadOnlyList<string> RecentPaths { get; init; } = Array.Empty<string>();

    public bool IsPreviewVisible { get; init; }
    public double PreviewWidth { get; init; } = 280;

    /// <summary>
    /// Whether the folders pane on the left is on screen at all. Off, the
    /// pane is put away with its width kept (<see cref="FoldersWidth"/>),
    /// so showing it again brings it back as it was. On for a file written
    /// before this field existed.
    /// </summary>
    public bool IsFoldersVisible { get; init; } = true;

    /// <summary>
    /// Width of the folders pane on the left, in pixels - a share of
    /// <see cref="LayoutWindowWidth"/> like the two sizes beside it. The
    /// default is the grid's own; a file written before this field existed
    /// reads as it.
    /// </summary>
    public double FoldersWidth { get; init; } = 280;

    /// <summary>
    /// Collapsed state of the bookmarks panel itself (the section above
    /// the drives tree). Defaults to expanded for new users — discovery
    /// matters more than chrome conservation on first run.
    /// </summary>
    public bool IsBookmarksExpanded { get; init; } = true;

    /// <summary>
    /// Height of the bookmarks region in the left pane, in pixels — where
    /// the user last dragged the divider between bookmarks and drives.
    /// </summary>
    public double BookmarksHeight { get; init; } = 200;

    /// <summary>
    /// Size of the window when the two sizes above were written. What the
    /// pane sizes are a share of: without it a pane left at 748 px of a
    /// 1925 px window comes back at 748 px in a 1086 px window and leaves
    /// the file list thirty pixels wide - a laptop after a monitor. Zero
    /// on a file written before this field existed, which
    /// <c>PaneSizes.Restore</c> reads as "nothing to scale by".
    /// </summary>
    public double LayoutWindowWidth { get; init; }

    /// <summary>Window height when the sizes above were written; see <see cref="LayoutWindowWidth"/>.</summary>
    public double LayoutWindowHeight { get; init; }
}


/// <summary>
/// Where the list stood in a folder, for coming back to it the next session
/// (2026-09-25): the file it was on - its main selected row - and the row
/// first on screen. The folder is gone: its nearest survivor opens as it
/// always did, and this is not used. The file is gone: the row that took its
/// place is selected, found among the rows that stood around it
/// (<c>CurrentRowFallback</c>); none of those left either -
/// the folder from its top, nothing selected.
/// </summary>
public sealed record ListPlace {
    /// <summary>How many rows on each side of the file are kept to find the one that took its place.</summary>
    public const int Neighbors = 8;


    /// <summary>The file the list was on; null when nothing was selected.</summary>
    public string? Row { get; init; }

    /// <summary>The rows around <see cref="Row"/> as they stood, in order, <see cref="Row"/> among them.</summary>
    public IReadOnlyList<string> StoodAmong { get; init; } = Array.Empty<string>();

    /// <summary>The row first on screen; null when it is not known.</summary>
    public string? Top { get; init; }


    /// <summary>The place in <paramref name="rows"/>, or null when there is nothing to remember.</summary>
    /// <param name="rows">The list's rows, in order.</param>
    /// <param name="row">The file it is on.</param>
    /// <param name="top">The row first on screen.</param>
    public static ListPlace? Of(IReadOnlyList<string> rows, string? row, string? top) {
        int at = row is null ? -1 : IndexOf(rows, row);
        string? first = top is null || IndexOf(rows, top) < 0 ? null : top;
        if (at < 0 && first is null) {
            return null;
        }

        int from = Math.Max(0, at - Neighbors);
        int to = at < 0 ? -1 : Math.Min(rows.Count - 1, at + Neighbors);

        return new ListPlace {
            Row = at < 0 ? null : rows[at],
            StoodAmong = at < 0 ? Array.Empty<string>() : rows.Skip(from).Take(to - from + 1).ToArray(),
            Top = first,
        };
    }

    /// <summary>The same place as <paramref name="other"/>: the same file, the same row on top.</summary>
    public bool SameAs(ListPlace? other) {
        return other is not null
            && string.Equals(Row, other.Row, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Top, other.Top, StringComparison.OrdinalIgnoreCase);
    }


    private static int IndexOf(IReadOnlyList<string> rows, string path) {
        for (int i = 0; i < rows.Count; i++) {
            if (string.Equals(rows[i], path, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }
}


/// <summary>
/// Legacy shape of one pinned view (see <see cref="SessionState.ManualViewModes"/>).
/// The mode is a string so that a reordered enum cannot silently
/// reinterpret what was saved.
/// </summary>
public sealed record FolderViewMode(string Path, string Mode);


/// <summary>
/// Window placement remembered between sessions. Separate from
/// <see cref="AppState"/> so the geometry block can grow (multi-monitor
/// info, splitter widths, …) without thrashing the top-level shape.
/// </summary>
public sealed record WindowGeometry {
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool Maximized { get; init; }
}
