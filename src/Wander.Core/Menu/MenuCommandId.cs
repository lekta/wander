namespace Wander.Core.Menu;

/// <summary>
/// Stable identity of a built-in context-menu entry. Two consumers depend
/// on these names:
///
///  - <c>Wander.App</c> maps an id to the <c>ICommand</c> that runs it, so
///    the Core layer never touches WPF;
///  - <c>AppSettings.HiddenContextMenuItems</c> persists them **by name**,
///    which is why members must not be renamed casually — a rename silently
///    resurrects an item the user had hidden.
///
/// Third-party (shell-extension) entries are not in this enum; they carry
/// <see cref="MenuEntry.ShellCommand"/> instead and are identified by their
/// header text.
/// </summary>
public enum MenuCommandId {
    None = 0,

    // --- Submenu headers ------------------------------------------------
    OpenSubmenu,
    FileSubmenu,
    ViewSubmenu,
    SortSubmenu,

    // --- Open group -----------------------------------------------------
    Open,
    OpenWith,
    OpenInTerminal,

    // --- Clipboard / file ops -------------------------------------------
    Cut,
    Copy,
    Paste,
    CopyPath,
    CopyName,
    CreateShortcut,

    // --- Mutations ------------------------------------------------------
    Rename,
    Delete,
    NewFolder,

    // --- View / sort ----------------------------------------------------
    ViewDetails,
    ViewTiles,
    ViewLargeIcons,
    TogglePreview,
    SortByName,
    SortByDate,
    SortBySize,
    SortByType,
    SortAscending,
    SortFoldersFirst,

    // --- Recycle bin ----------------------------------------------------
    RestoreFromRecycleBin,

    // --- Misc -----------------------------------------------------------
    Refresh,
    Undo,
    Properties,
}
