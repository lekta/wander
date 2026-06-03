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


    // --- Debug ---------------------------------------------------------
    /// <summary>Whether the "Debug" submenu is visible in the main menu.</summary>
    public bool ShowDebugMenu { get; init; } = true;
}
