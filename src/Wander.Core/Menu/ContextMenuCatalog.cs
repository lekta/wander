namespace Wander.Core.Menu;

/// <summary>
/// Single source of truth for what every built-in entry is called and which
/// hotkey it advertises. Both the builder and the settings dialog read from
/// here, so a label never drifts between the menu and the checkbox that
/// hides it.
///
/// <para>
/// Labels are English to match the menu bar and the file list this menu
/// pops up over; the settings dialog around it is Russian. That split is
/// pre-existing (see the app's main menu) and not something this catalog
/// tries to resolve.
/// </para>
/// </summary>
public static class ContextMenuCatalog {
    private static readonly Dictionary<MenuCommandId, string> _titles = new() {
        [MenuCommandId.FileSubmenu] = "File",
        [MenuCommandId.ViewSubmenu] = "View",
        [MenuCommandId.SortSubmenu] = "Sort by",
        [MenuCommandId.ShellSubmenu] = "More options",

        [MenuCommandId.Open] = "Open",
        [MenuCommandId.OpenWith] = "Open with...",
        [MenuCommandId.OpenInExplorer] = "Show in Explorer",
        [MenuCommandId.OpenInTerminal] = "Open in Terminal",

        [MenuCommandId.Cut] = "Cut",
        [MenuCommandId.Copy] = "Copy",
        [MenuCommandId.Paste] = "Paste",
        [MenuCommandId.CopyPath] = "Copy path",
        [MenuCommandId.CopyName] = "Copy name",
        [MenuCommandId.CreateShortcut] = "Create shortcut",

        [MenuCommandId.Rename] = "Rename",
        [MenuCommandId.Delete] = "Delete",
        [MenuCommandId.PermanentDelete] = "Delete permanently",
        [MenuCommandId.NewFolder] = "New folder",

        [MenuCommandId.ViewDetails] = "Details",
        [MenuCommandId.ViewTiles] = "Tiles",
        [MenuCommandId.ViewLargeIcons] = "Large icons",
        [MenuCommandId.TogglePreview] = "Preview pane",
        [MenuCommandId.SortByName] = "Name",
        [MenuCommandId.SortByDate] = "Date modified",
        [MenuCommandId.SortBySize] = "Size",
        [MenuCommandId.SortByType] = "Type",
        [MenuCommandId.SortAscending] = "Ascending",
        [MenuCommandId.SortFoldersFirst] = "Folders first",

        [MenuCommandId.Refresh] = "Refresh",
        [MenuCommandId.Undo] = "Undo",
        [MenuCommandId.AddBookmark] = "Add to bookmarks",
        [MenuCommandId.Properties] = "Properties",
    };

    private static readonly Dictionary<MenuCommandId, string> _gestures = new() {
        [MenuCommandId.Cut] = "Ctrl+X",
        [MenuCommandId.Copy] = "Ctrl+C",
        [MenuCommandId.Paste] = "Ctrl+V",
        [MenuCommandId.CopyPath] = "Ctrl+Shift+C",
        [MenuCommandId.Rename] = "F2",
        [MenuCommandId.Delete] = "Del",
        [MenuCommandId.PermanentDelete] = "Shift+Del",
        [MenuCommandId.NewFolder] = "Ctrl+Shift+N",
        [MenuCommandId.Refresh] = "F5",
        [MenuCommandId.Undo] = "Ctrl+Z",
        [MenuCommandId.Properties] = "Alt+Enter",
        [MenuCommandId.Open] = "Enter",
    };

    /// <summary>
    /// Entries the settings dialog offers to hide, in the order they should
    /// be listed there. Submenu headers are included on purpose — hiding
    /// "File" removes the whole clipboard block in one click, which is
    /// exactly what a hotkey-only user wants.
    ///
    /// <para>
    /// Excluded: <see cref="MenuCommandId.ShellSubmenu"/> (governed by the
    /// separate extensions switch) and the View / Sort leaves (hiding
    /// individual sort keys is noise — hide the submenu instead).
    /// </para>
    /// </summary>
    public static IReadOnlyList<MenuCommandId> Hideable { get; } = new[] {
        MenuCommandId.Open,
        MenuCommandId.OpenWith,
        MenuCommandId.OpenInExplorer,
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
        MenuCommandId.PermanentDelete,
        MenuCommandId.NewFolder,
        MenuCommandId.ViewSubmenu,
        MenuCommandId.SortSubmenu,
        MenuCommandId.Refresh,
        MenuCommandId.Undo,
        MenuCommandId.AddBookmark,
        MenuCommandId.Properties,
    };


    public static string Title(MenuCommandId id) {
        return _titles.TryGetValue(id, out string? title) ? title : id.ToString();
    }

    public static string? Gesture(MenuCommandId id) {
        return _gestures.TryGetValue(id, out string? gesture) ? gesture : null;
    }
}
