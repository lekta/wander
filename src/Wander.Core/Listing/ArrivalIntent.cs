namespace Wander.Core.Listing;

/// <summary>What the selection is supposed to do when a listing lands.</summary>
public enum ArrivalAction {
    /// <summary>Select rows inside the listing.</summary>
    SelectRows,

    /// <summary>
    /// Select the listed folder itself. A folder is never a row in its own
    /// listing, so this is a different move rather than a special case of
    /// the one above — it is what clicking a row in the tree means.
    /// </summary>
    SelectFolderItself,
}


/// <summary>
/// One deferred answer to "what should be selected when the folder finishes
/// listing" — and there is exactly one at a time, on purpose.
///
/// <para>
/// There used to be four fields asking that question, written from nine
/// places and read by three methods standing in a row. When two were set,
/// the winner was whichever method came last in the file: a decision nobody
/// made, that fell out of the order of the lines. Setting an intent now
/// <em>replaces</em> the pending one, and there is one place that applies it.
/// </para>
/// </summary>
/// <param name="Action">Rows inside the listing, or the listed folder itself.</param>
/// <param name="Paths">What to select.</param>
/// <param name="ForFolder">
/// The folder whose listing this intent is waiting for. A listing for
/// anything else neither applies nor consumes it, and a navigation somewhere
/// else drops it: an intent that outlived its folder would select something
/// the user is no longer looking at.
/// </param>
/// <param name="TakeFocus">
/// Whether the list should take the keyboard back along with the selection.
/// Set by operations that ran behind a modal dialog — by the time it closes,
/// the row that had the keyboard has been rebuilt out of existence - and by
/// the last session's place (<see cref="Place"/>).
/// </param>
/// <param name="RenameTarget">
/// A path whose inline editor should open once it is selected. Only ever the
/// row that was asked for: a listing that landed for some other reason must
/// not open an editor under the user's hands.
/// </param>
/// <param name="StoodAmong">
/// The rows around the wanted one as they stood when it was noted, in
/// order. None of the wanted rows listed, the one that took their place is
/// selected instead (<see cref="CurrentRowFallback"/>); null - nothing is.
/// </param>
/// <param name="Top">The row to put first on screen along with the selection; null to only bring the selection into view.</param>
public sealed record ArrivalIntent(
    ArrivalAction Action,
    IReadOnlyList<string> Paths,
    string? ForFolder,
    bool TakeFocus = false,
    string? RenameTarget = null,
    IReadOnlyList<string>? StoodAmong = null,
    string? Top = null) {

    /// <summary>Select these rows once <paramref name="folder"/> has listed.</summary>
    public static ArrivalIntent Rows(
        string folder, IReadOnlyList<string> paths, bool takeFocus = false, string? renameTarget = null) {
        return new ArrivalIntent(ArrivalAction.SelectRows, paths, folder, takeFocus, renameTarget);
    }


    /// <summary>
    /// The list as the last session left it in <paramref name="folder"/>
    /// (2026-09-25): <paramref name="row"/> selected - or the row that took
    /// its place among <paramref name="stoodAmong"/> - and <paramref name="top"/>
    /// first on screen. The keyboard goes onto it: the window comes up with
    /// the keyboard on itself, where the arrows move nothing.
    /// </summary>
    public static ArrivalIntent Place(string folder, string? row, IReadOnlyList<string> stoodAmong, string? top) {
        return new ArrivalIntent(
            ArrivalAction.SelectRows, row is null ? Array.Empty<string>() : new[] { row }, folder,
            TakeFocus: true, StoodAmong: stoodAmong, Top: top);
    }


    /// <summary>Select the folder itself once it has listed.</summary>
    public static ArrivalIntent Folder(string path) {
        return new ArrivalIntent(ArrivalAction.SelectFolderItself, new[] { path }, path);
    }
}
