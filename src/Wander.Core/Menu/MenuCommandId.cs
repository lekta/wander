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
///
/// <para>
/// Refresh and undo are deliberately absent: they are window-wide, they
/// live in the toolbar's "Вид" menu and on hotkeys. The view and the order
/// are the open folder's own and came back to its empty space (2026-09-28)
/// - Explorer's place for them; a user who finds them doubled switches the
/// two submenus off. Names dropped from this enum are ignored on load
/// rather than rejected - see <c>ContextMenuSettings.From</c>.
/// </para>
/// </summary>
public enum MenuCommandId {
    None = 0,

    // --- Submenu headers ------------------------------------------------
    OpenSubmenu,
    FileSubmenu,
    NewSubmenu,
    ActionsSubmenu,
    ConvertSubmenu,
    /// <summary>Inside the two above: the same actions, their outputs going to a folder the user picks.</summary>
    ToFolderSubmenu,

    // --- Open group -----------------------------------------------------
    Open,
    OpenWith,
    OpenInTerminal,
    /// <summary>A search result: to its folder, the row selected.</summary>
    GoToLocation,

    // --- Clipboard / file ops -------------------------------------------
    Cut,
    Copy,
    Paste,
    CopyPath,
    CopyName,
    CreateShortcut,

    // --- Mutations ------------------------------------------------------
    Rename,
    BatchRename,
    Delete,
    NewFolder,

    // --- Custom actions (Wander.Core.Actions) ---------------------------
    /// <summary>One catalog action; <c>MenuEntry.Argument</c> carries its id.</summary>
    RunAction,
    /// <summary>The same action, asking for the folder its outputs go to first.</summary>
    RunActionTo,
    /// <summary>Placeholder row: the submenu has nothing for this selection.</summary>
    NoActions,
    /// <summary>Opens the settings page that edits the catalog.</summary>
    ConfigureActions,

    // --- Archives -------------------------------------------------------
    Extract,
    /// <summary>Into the folder the archive sits in, asking nothing (2026-09-23).</summary>
    ExtractHere,

    // --- Recycle bin ----------------------------------------------------
    RestoreFromRecycleBin,

    // --- The open folder's view and order (background menu) -------------
    ViewSubmenu,
    SortSubmenu,
    /// <summary>One of the views, pinned to the folder; <c>MenuEntry.Argument</c> carries the <c>ViewMode</c> name.</summary>
    SetView,
    /// <summary>The folder's view chosen for it again - its pin taken off.</summary>
    ViewAuto,
    /// <summary>The view on screen becomes the default one.</summary>
    MakeDefaultView,
    /// <summary>One of the keys, pinned to the folder with the rest of its order; <c>MenuEntry.Argument</c> carries the <c>SortKey</c> name.</summary>
    SetSortKey,
    SortAscending,
    SortFoldersFirst,
    /// <summary>The folder in the default order again - its pin taken off.</summary>
    SortAuto,
    /// <summary>The order on screen becomes the default one.</summary>
    MakeDefaultSort,

    // --- Misc -----------------------------------------------------------
    Properties,

    // --- Drop with the right mouse button (DropMenuBuilder) -------------
    /// <summary>The first row: what was dropped and where. Never enabled.</summary>
    DropCaption,
    DropCopyHere,
    DropMoveHere,
    DropLinkHere,
    DropCancel,
}
