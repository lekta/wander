using Wander.Core.FileSystem;

namespace Wander.Core.Persistence;

/// <summary>
/// User-tunable preferences. Distinct from <see cref="AppState"/> session
/// fields (last folder, expanded tree, window geometry) — those describe
/// "where the user left off", while <see cref="AppSettings"/> describes
/// "how the user wants Wander to behave". Both are persisted together in
/// <c>state.json</c> for now; can split into a separate file later if the
/// settings record grows.
///
/// Default values represent the out-of-the-box behaviour: paranoid about
/// deletes, hides Windows hidden/system files (Explorer-parity), shows
/// the developer Debug menu (we're pre-1.0). LargeIcons defaults match
/// the values that lived inline in MainWindow.xaml before this record
/// existed.
/// </summary>
public sealed record AppSettings {
    // --- General -------------------------------------------------------
    /// <summary>Restore the last folder on launch instead of going to the first drive.</summary>
    public bool RestoreLastFolder { get; init; } = true;


    // --- Safety --------------------------------------------------------
    /// <summary>Show files / folders with the <c>Hidden</c> attribute.</summary>
    public bool ShowHidden { get; init; } = false;

    /// <summary>Show files / folders with the <c>System</c> attribute.</summary>
    public bool ShowSystem { get; init; } = false;


    // --- File operations ----------------------------------------------
    /// <summary>
    /// Ask for confirmation before moving items to the recycle bin.
    /// When off, Delete (no Shift) just sends the items to the bin straight
    /// away — Ctrl+Z still restores them, so the safety net remains. Shift+Delete
    /// (permanent) always confirms regardless.
    /// </summary>
    public bool ConfirmRecycle { get; init; } = true;


    // --- Companions ("integrated items") -------------------------------
    /// <summary>
    /// Fold companion files (Unity <c>.meta</c>, RawTherapee <c>.pp3</c>)
    /// into the file they belong to: one row in the listing, and every
    /// operation on the main file takes its sidecars along. On by default —
    /// a sidecar left behind by a rename is a bug from the user's side of
    /// the screen, whichever tool caused it.
    /// </summary>
    public bool IntegrateCompanions { get; init; } = true;


    // --- Sort ----------------------------------------------------------
    /// <summary>Which column the folder listing is sorted by.</summary>
    public SortKey SortKey { get; init; } = SortKey.Name;

    /// <summary>Sort direction: true = A→Z / oldest→newest / smallest→largest.</summary>
    public bool SortAscending { get; init; } = true;

    /// <summary>Group folders (and folder-like shortcuts) above plain files.</summary>
    public bool GroupFoldersFirst { get; init; } = true;


    // --- Layout (LargeIcons view) -------------------------------------
    /// <summary>Width of one tile in the LargeIcons grid, in pixels.</summary>
    public int LargeIconCellWidth { get; init; } = 100;

    /// <summary>Side length of the icon image inside a tile, in pixels.</summary>
    public int LargeIconImageSize { get; init; } = 72;

    /// <summary>Margin around each tile (all four sides), in pixels.</summary>
    public int LargeIconMargin { get; init; } = 2;

    /// <summary>Font size of the file-name label under each tile.</summary>
    public int LargeIconLabelFontSize { get; init; } = 12;


    // --- Bookmarks -----------------------------------------------------
    /// <summary>Show the user's Downloads folder as a default bookmark.</summary>
    public bool ShowBookmarkDownloads { get; init; } = true;

    /// <summary>Show the user's Documents folder as a default bookmark.</summary>
    public bool ShowBookmarkDocuments { get; init; } = true;

    /// <summary>Show the user's Pictures folder as a default bookmark.</summary>
    public bool ShowBookmarkPictures { get; init; } = true;

    /// <summary>Show the Recycle Bin as a default bookmark (read-only browsing).</summary>
    public bool ShowBookmarkRecycleBin { get; init; } = true;


    // --- Context menu --------------------------------------------------
    /// <summary>
    /// Show entries contributed by third-party shell extensions (7-Zip,
    /// TortoiseGit, …). On by default — parity with Explorer is the point.
    /// Turning it off also stops Wander from loading those handlers' DLLs
    /// at all, which is the reason someone might want it off.
    /// </summary>
    public bool ShellExtensionsEnabled { get; init; } = true;

    /// <summary>
    /// Third-party entries the user switched off, by display name. Names
    /// rather than CLSIDs because <c>IContextMenu</c> never tells us which
    /// handler produced a given row.
    /// </summary>
    public IReadOnlyList<string> BlockedShellExtensions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Third-party entry names met so far, so the settings dialog can offer
    /// a checkbox list instead of a free-text field. Grows as the user opens
    /// menus in different folders; strictly a convenience cache — deleting
    /// it costs nothing but re-discovery.
    /// </summary>
    public IReadOnlyList<string> KnownShellExtensions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Built-in context-menu entries the user hid, as
    /// <c>MenuCommandId</c> names. Stored as strings so a reordered or
    /// extended enum doesn't reinterpret the saved list.
    /// </summary>
    public IReadOnlyList<string> HiddenContextMenuItems { get; init; } = Array.Empty<string>();


    // --- Debug ---------------------------------------------------------
    /// <summary>Whether the "Debug" submenu is visible in the main menu.</summary>
    public bool ShowDebugMenu { get; init; } = true;
}
