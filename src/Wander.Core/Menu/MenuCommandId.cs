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
/// View mode, sorting, refresh and undo are deliberately absent: they are
/// window-wide state, they live in the toolbar's «Вид» menu and on hotkeys,
/// and a right-click on a folder is not where you go looking for them.
/// Names dropped from this enum are ignored on load rather than rejected —
/// see <c>ContextMenuSettings.From</c>.
/// </para>
/// </summary>
public enum MenuCommandId {
    None = 0,

    // --- Submenu headers ------------------------------------------------
    OpenSubmenu,
    FileSubmenu,
    NewSubmenu,

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

    // --- Recycle bin ----------------------------------------------------
    RestoreFromRecycleBin,

    // --- Misc -----------------------------------------------------------
    Properties,
}
