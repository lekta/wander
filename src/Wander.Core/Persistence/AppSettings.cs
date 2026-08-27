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


    // --- Behaviour -----------------------------------------------------
    /// <summary>
    /// Re-list the current folder by itself when something changes it from
    /// outside — another application saving a file, a download finishing, a
    /// copy done in Explorer. On by default: a listing that quietly lies
    /// until F5 is one of the things this project exists to fix. Off is for
    /// the rare folder where the churn is constant and the re-listing is
    /// more distracting than the staleness.
    /// </summary>
    public bool AutoRefresh { get; init; } = true;


    // --- Safety --------------------------------------------------------
    /// <summary>Show files / folders with the <c>Hidden</c> attribute.</summary>
    public bool ShowHidden { get; init; } = false;

    /// <summary>Show files / folders with the <c>System</c> attribute.</summary>
    public bool ShowSystem { get; init; } = false;

    /// <summary>
    /// Hide the volume-root bookkeeping folders (<c>$RECYCLE.BIN</c>,
    /// <c>System Volume Information</c>, …) listed in
    /// <see cref="SystemRootFolders"/>, even when <see cref="ShowSystem"/>
    /// is on. Someone who switches system files on wants to see their own
    /// hidden files, not Windows' per-volume plumbing they cannot open
    /// anyway.
    /// </summary>
    public bool HideSystemRootFolders { get; init; } = true;


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


    // --- Layout (Details view) ----------------------------------------
    //
    // Each view owns its own sizes: what makes a comfortable table is not
    // what makes a comfortable grid of photographs, and a single "icon size"
    // shared between them would be wrong in at least one of them. The three
    // groups below are independent on purpose.
    //
    // The numbers are Explorer's, per view — the point of the project is to
    // be a better Explorer, not a differently-proportioned one, and someone
    // switching between the two should not have to re-learn how much fits on
    // a screen.

    /// <summary>Height of one row in the Details table, in pixels.</summary>
    public int DetailsRowHeight { get; init; } = 22;

    /// <summary>Side of the icon in the Details table's first column, in pixels.</summary>
    public int DetailsIconSize { get; init; } = 16;


    // --- Layout (Tiles view) ------------------------------------------
    /// <summary>Width of one tile in the Tiles grid, in pixels.</summary>
    public int TileCellWidth { get; init; } = 260;

    /// <summary>Side of the icon inside a tile, in pixels.</summary>
    public int TileIconSize { get; init; } = 48;

    /// <summary>Font size of the file-name line inside a tile.</summary>
    public int TileLabelFontSize { get; init; } = 12;


    // --- Layout (LargeIcons view) -------------------------------------
    /// <summary>
    /// Width of one tile in the LargeIcons grid, in pixels. The 36 px it
    /// leaves around a 96 px icon is the air Explorer leaves — wider looks
    /// sparse, narrower starts cutting names that would otherwise fit.
    /// </summary>
    public int LargeIconCellWidth { get; init; } = 132;

    /// <summary>Side length of the icon image inside a tile, in pixels.</summary>
    public int LargeIconImageSize { get; init; } = 96;

    /// <summary>Margin around each tile (all four sides), in pixels.</summary>
    public int LargeIconMargin { get; init; } = 2;

    /// <summary>Font size of the file-name label under each tile.</summary>
    public int LargeIconLabelFontSize { get; init; } = 12;


    // --- Thumbnail cache ------------------------------------------------
    /// <summary>
    /// Keep generated thumbnails in <c>%LocalAppData%\Wander\thumbs</c> so
    /// they survive a restart. On by default: the folder that hurts without
    /// it is a folder of RAW photos, and there the difference is seconds
    /// per visit.
    /// </summary>
    public bool ThumbnailDiskCacheEnabled { get; init; } = true;

    /// <summary>
    /// Ceiling on that folder, in megabytes. 256 MB holds a few thousand
    /// thumbnails — enough for the folders someone actually revisits,
    /// small enough that nobody notices it on a system drive.
    /// </summary>
    public int ThumbnailDiskCacheMb { get; init; } = 256;

    /// <summary>
    /// How many thumbnails to hold in RAM. At roughly 100 KB apiece, 512 is
    /// tens of megabytes — a full screen of tiles many times over.
    /// </summary>
    public int ThumbnailMemoryEntries { get; init; } = 512;


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
