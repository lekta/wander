using Wander.Core.Companions;
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

    /// <summary>
    /// Show protected operating system files: the ones carrying
    /// <c>Hidden</c> and <c>System</c> together, which is what Windows
    /// means by the phrase and what Explorer's own checkbox covers. The
    /// <c>System</c> attribute alone is not enough — see
    /// <see cref="EntryVisibility.Allows"/>.
    /// </summary>
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


    // --- Navigation ----------------------------------------------------
    /// <summary>
    /// Whether moving the tree cursor with the arrow keys also opens the
    /// folder it lands on.
    ///
    /// <para>
    /// Off by default, which is deliberately not Explorer's behaviour:
    /// Explorer navigates on every selection change, so arrowing past ten
    /// folders on the way to the eleventh lists all ten — ten directory
    /// reads, ten thumbnail passes, and the list position lost each time.
    /// Wander moves for free and opens on <c>Enter</c> or a click. The
    /// switch is here because the other habit is a real habit, and someone
    /// who has it should not have to unlearn it.
    /// </para>
    /// </summary>
    public bool TreeKeyboardNavigates { get; init; } = false;


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


    // --- Layout (Gallery view) -----------------------------------------
    /// <summary>
    /// Width of one gallery cell, in pixels. The 16 px it leaves around a
    /// 200 px picture is deliberately tighter than the LargeIcons grid: a
    /// wall of photographs reads as a wall when the gaps are thin, and as a
    /// list of framed items when they are not.
    /// </summary>
    public int GalleryCellWidth { get; init; } = 216;

    /// <summary>Side of the square a gallery picture is drawn in, in pixels.</summary>
    public int GalleryImageSize { get; init; } = 200;

    /// <summary>Margin around each gallery cell (all four sides), in pixels.</summary>
    public int GalleryMargin { get; init; } = 4;

    /// <summary>Font size of the name under a gallery picture.</summary>
    public int GalleryLabelFontSize { get; init; } = 11;

    /// <summary>
    /// What the gallery draws behind the pictures. The window's own
    /// background by default: a view that opens dark the first time reads
    /// as a theme somebody turned on, not as a tool the user is about to
    /// choose. The grey most photographers want is one click away and
    /// persists once chosen.
    /// </summary>
    public GalleryBackground GalleryBackground { get; init; } = GalleryBackground.Light;

    /// <summary>
    /// Lightness of the "grey" gallery background, 0…255. Settable because
    /// the right neutral depends on the room and the monitor as much as on
    /// the photographs; 110 is the usual mid grey.
    /// </summary>
    public int GalleryGreyLevel { get; init; } = 110;

    /// <summary>
    /// Lightness of the "dark" gallery background, 0…255. Not zero on
    /// purpose: against pure black the edges of a dark photograph vanish.
    /// </summary>
    public int GalleryDarkLevel { get; init; } = 30;

    /// <summary>
    /// Switch to the gallery by itself on entering a folder that is mostly
    /// pictures (see <see cref="Wander.Core.Listing.ImageFolderProbe"/>).
    /// On by default: the whole point is not having to ask for the right
    /// view in the folder where it is obvious. Choosing a view by hand in a
    /// folder turns the automation off for that folder permanently — an
    /// automatic choice that overrules an explicit one is not a convenience.
    /// </summary>
    public bool AutoGallery { get; init; } = true;

    /// <summary>
    /// How much of a folder has to be pictures before the gallery switches
    /// itself on, in per cent of the <em>content</em> files (see
    /// <see cref="Wander.Core.Listing.ImageFolderProbe"/> for what does
    /// not count). Strictly more than this share.
    /// </summary>
    public int AutoGalleryPercent { get; init; } = 50;


    // --- Ratings --------------------------------------------------------
    /// <summary>
    /// Which sidecar to create when the user rates a photo that has none.
    /// XMP by default, and the choice is not cosmetic — see
    /// <see cref="SidecarFormat.Pp3"/> for what creating a <c>.pp3</c> does
    /// to how RawTherapee develops the photo.
    /// </summary>
    public SidecarFormat RawRatingFormat { get; init; } = SidecarFormat.Xmp;


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

    /// <summary>Show the user's Desktop folder as a default bookmark.</summary>
    public bool ShowBookmarkDesktop { get; init; } = false;

    /// <summary>Show the user's Music folder as a default bookmark.</summary>
    public bool ShowBookmarkMusic { get; init; } = false;

    /// <summary>Show the user's Videos folder as a default bookmark.</summary>
    public bool ShowBookmarkVideos { get; init; } = false;

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
    /// Third-party entries the user switched off, by
    /// <c>ShellEntryKey</c> — the canonical verb where the handler
    /// publishes one, the normalised label otherwise. Not CLSIDs, because
    /// <c>IContextMenu</c> never tells us which handler produced a row.
    /// </summary>
    public IReadOnlyList<string> BlockedShellExtensions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Menu rows Wander has actually met, with what the row said about
    /// itself at the time. The registry knows which handlers are installed
    /// but not what a COM handler will draw; this is the other half, and it
    /// is the only source for the description column — a handler publishes
    /// its help text through <c>IContextMenu</c> and nowhere else.
    /// A convenience cache: deleting it costs nothing but re-discovery.
    /// </summary>
    public IReadOnlyList<KnownShellEntry> KnownShellEntries { get; init; } = Array.Empty<KnownShellEntry>();

    /// <summary>
    /// Show Windows' own context-menu plumbing in the settings table.
    /// Off by default: roughly forty of the fifty handlers on a stock
    /// machine are BitLocker verbs, Defender, Work Folders and the sharing
    /// menu, and listing them buries the handful anyone came to switch off.
    /// </summary>
    public bool ShowSystemShellExtensions { get; init; } = false;

    /// <summary>
    /// Registry scopes the user added to the table by hand, beyond the base
    /// ones (<c>ShellScopes.Base</c>) that are always scanned. This is what
    /// the "Добавить" button writes: pick a file type and its handlers join
    /// the table; pick an application and every scope it registers on does.
    /// </summary>
    public IReadOnlyList<string> TrackedShellScopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The last few file types a context menu was opened on, newest first.
    /// Feeds one thing only — the "Добавить" picker leads with them instead
    /// of with eight hundred extensions in alphabetical order. Losing it
    /// costs a scroll; see <c>RecentScopes</c>.
    /// </summary>
    public IReadOnlyList<string> RecentShellScopes { get; init; } = Array.Empty<string>();

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


/// <summary>
/// One context-menu row as Wander met it, remembered between sessions.
///
/// <para>
/// Everything except <see cref="Key"/> is display material: it exists so the
/// settings table can say what a row is called and what it does without
/// having to open a menu first. None of it is used to match anything —
/// titles and help are localised and change with the file, which is exactly
/// why the key is separate.
/// </para>
/// </summary>
public sealed record KnownShellEntry {
    /// <summary>The blocklist handle — see <c>ShellEntryKey</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The row's label as drawn, minus Win32 decoration.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The handler's own description, or empty where it published none.</summary>
    public string Help { get; init; } = string.Empty;

    /// <summary>Where it was seen: an extension, or one of the base scopes.</summary>
    public string Scope { get; init; } = string.Empty;
}
