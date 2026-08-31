namespace Wander.Core.Navigation;

/// <summary>
/// Where a navigation came from. Carried alongside the destination in
/// <see cref="NavigationService"/> history so Back / Forward can replay
/// not just the path but also the *panel context* the user was browsing
/// from — e.g., returning to a path that was originally opened via the
/// bookmarks panel re-expands the bookmarks tree rather than the drives
/// tree.
/// </summary>
public enum NavigationSource {
    /// <summary>Click on the drives tree (left pane, lower section).</summary>
    Drives,

    /// <summary>Click on the bookmarks tree (left pane, upper section).</summary>
    Bookmark,

    /// <summary>Enter typed in the address bar.</summary>
    Address,

    /// <summary>Double-click on a folder in the right pane.</summary>
    RightPane,

    /// <summary>Restored from <c>state.json</c> on startup.</summary>
    Restore,

    /// <summary>Any other path — drag&amp;drop target follow, scripting, default first-drive, …</summary>
    External,
}


/// <summary>
/// One step in the navigation history: the destination path and the
/// gesture that brought the user there.
/// </summary>
internal sealed record NavigationEntry(string Path, NavigationSource Source);


/// <summary>
/// Persistable pairing of a filesystem path with the panel it "belongs"
/// to — used both for <c>AppState.LastPath</c> (where the user closed
/// the session) and each entry in <c>AppState.ExpandedPaths</c> (which
/// node in which panel was expanded). Same record, two consumers; lets
/// the restore code re-expand the bookmarks-side and drives-side trees
/// independently rather than dedup'ing by raw path and losing the
/// panel context.
/// </summary>
public sealed record NavigationStop(string Path, NavigationSource Source);
