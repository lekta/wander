using Wander.Core.Localization;

namespace Wander.Core.Menu;

/// <summary>
/// Single source of truth for what every built-in entry is called and which
/// hotkey it advertises. Both the builder and the settings dialog read from
/// here, so a label never drifts between the menu and the checkbox that
/// hides it.
///
/// <para>
/// What lives here is the *structure* — which entry exists, what hotkey it
/// advertises, which ones the settings dialog may hide — plus the resource
/// key of each label. The label text itself is in the app's string table
/// (<c>Resources/Strings.resx</c>) and is fetched through
/// <see cref="Text"/>: Core has no reference to the app layer, and adding
/// a language must not mean editing Core.
/// </para>
///
/// <para>
/// With no text source registered (which is the case in tests) a key comes
/// back as itself. The drift guards still work — a key is visibly not a
/// label — without every test having to set up localisation.
/// </para>
/// </summary>
public static class ContextMenuCatalog {
    private static readonly Dictionary<MenuCommandId, string> _titleKeys = new() {
        [MenuCommandId.OpenSubmenu] = "MenuCmdOpenSubmenu",
        [MenuCommandId.FileSubmenu] = "MenuCmdFileSubmenu",
        [MenuCommandId.ViewSubmenu] = "MenuCmdViewSubmenu",
        [MenuCommandId.SortSubmenu] = "MenuCmdSortSubmenu",

        [MenuCommandId.Open] = "MenuCmdOpen",
        [MenuCommandId.OpenWith] = "MenuCmdOpenWith",
        [MenuCommandId.OpenInTerminal] = "MenuCmdOpenInTerminal",

        [MenuCommandId.Cut] = "MenuCmdCut",
        [MenuCommandId.Copy] = "MenuCmdCopy",
        [MenuCommandId.Paste] = "MenuCmdPaste",
        [MenuCommandId.CopyPath] = "MenuCmdCopyPath",
        [MenuCommandId.CopyName] = "MenuCmdCopyName",
        [MenuCommandId.CreateShortcut] = "MenuCmdCreateShortcut",

        [MenuCommandId.Rename] = "MenuCmdRename",
        [MenuCommandId.Delete] = "MenuCmdDelete",
        [MenuCommandId.NewFolder] = "MenuCmdNewFolder",

        [MenuCommandId.ViewDetails] = "MenuCmdViewDetails",
        [MenuCommandId.ViewTiles] = "MenuCmdViewTiles",
        [MenuCommandId.ViewLargeIcons] = "MenuCmdViewLargeIcons",
        [MenuCommandId.TogglePreview] = "MenuCmdTogglePreview",
        [MenuCommandId.SortByName] = "MenuCmdSortByName",
        [MenuCommandId.SortByDate] = "MenuCmdSortByDate",
        [MenuCommandId.SortBySize] = "MenuCmdSortBySize",
        [MenuCommandId.SortByType] = "MenuCmdSortByType",
        [MenuCommandId.SortAscending] = "MenuCmdSortAscending",
        [MenuCommandId.SortFoldersFirst] = "MenuCmdSortFoldersFirst",

        [MenuCommandId.RestoreFromRecycleBin] = "MenuCmdRestore",

        [MenuCommandId.Refresh] = "MenuCmdRefresh",
        [MenuCommandId.Undo] = "MenuCmdUndo",
        [MenuCommandId.Properties] = "MenuCmdProperties",
    };

    private static readonly Dictionary<MenuCommandId, string> _gestures = new() {
        [MenuCommandId.Open] = "Enter",
        [MenuCommandId.Cut] = "Ctrl+X",
        [MenuCommandId.Copy] = "Ctrl+C",
        [MenuCommandId.Paste] = "Ctrl+V",
        [MenuCommandId.CopyPath] = "Ctrl+Shift+C",
        [MenuCommandId.Rename] = "F2",
        [MenuCommandId.Delete] = "Del",
        [MenuCommandId.NewFolder] = "Ctrl+Shift+N",
        [MenuCommandId.Refresh] = "F5",
        [MenuCommandId.Undo] = "Ctrl+Z",
        [MenuCommandId.Properties] = "Alt+Enter",
    };

    /// <summary>
    /// Entries the settings dialog offers to hide, in the order they should
    /// be listed there. Submenu headers are included on purpose — hiding
    /// "Файл" removes the whole clipboard block in one click, which is
    /// exactly what a hotkey-only user wants.
    ///
    /// <para>
    /// Excluded: the View / Sort leaves — hiding individual sort keys is
    /// noise, hide the submenu instead.
    /// </para>
    /// </summary>
    public static IReadOnlyList<MenuCommandId> Hideable { get; } = new[] {
        MenuCommandId.Open,
        MenuCommandId.OpenSubmenu,
        MenuCommandId.OpenWith,
        MenuCommandId.OpenInTerminal,
        MenuCommandId.FileSubmenu,
        MenuCommandId.Cut,
        MenuCommandId.Copy,
        MenuCommandId.Paste,
        MenuCommandId.CopyPath,
        MenuCommandId.CopyName,
        MenuCommandId.CreateShortcut,
        MenuCommandId.Rename,
        MenuCommandId.Delete,
        MenuCommandId.NewFolder,
        MenuCommandId.ViewSubmenu,
        MenuCommandId.SortSubmenu,
        MenuCommandId.Refresh,
        MenuCommandId.Undo,
        MenuCommandId.Properties,
    };


    /// <summary>
    /// Label for one entry. Unknown ids fall back to the enum name — an
    /// entry that reaches the menu without a key should be visible, not
    /// silently blank.
    /// </summary>
    public static string Title(MenuCommandId id) {
        return _titleKeys.TryGetValue(id, out string? key) ? Text.Get(key) : id.ToString();
    }

    public static string? Gesture(MenuCommandId id) {
        return _gestures.TryGetValue(id, out string? gesture) ? gesture : null;
    }
}
