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
        [MenuCommandId.NewSubmenu] = "MenuCmdNewSubmenu",

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

        [MenuCommandId.Extract] = "MenuCmdExtract",

        [MenuCommandId.RestoreFromRecycleBin] = "MenuCmdRestore",

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
        [MenuCommandId.Properties] = "Alt+Enter",
    };

    /// <summary>
    /// Entries the settings dialog offers to hide, in menu order and with
    /// the menu's own shape: a submenu header followed by its children, one
    /// level in. Flattening it was a small lie — "Вставить" and "Файл" are
    /// not siblings, and a list that says they are makes the reader wonder
    /// what unticking the second one does to the first.
    ///
    /// <para>
    /// Submenu headers are switchable themselves, on purpose: hiding "Файл"
    /// removes the whole clipboard block in one click, which is exactly what
    /// a hotkey-only user wants.
    /// </para>
    ///
    /// <para>
    /// Everything the builder emits is in here — <c>ContextMenuCatalogTests</c>
    /// pins that, so an entry can never reach the menu without a switch that
    /// turns it off.
    /// </para>
    /// </summary>
    public static IReadOnlyList<MenuNode> HideableTree { get; } = new[] {
        new MenuNode(MenuCommandId.Open, 0),
        new MenuNode(MenuCommandId.OpenSubmenu, 0),
        new MenuNode(MenuCommandId.OpenWith, 1),
        new MenuNode(MenuCommandId.OpenInTerminal, 0),
        new MenuNode(MenuCommandId.FileSubmenu, 0),
        new MenuNode(MenuCommandId.Cut, 1),
        new MenuNode(MenuCommandId.Copy, 1),
        new MenuNode(MenuCommandId.Paste, 1),
        new MenuNode(MenuCommandId.CopyPath, 1),
        new MenuNode(MenuCommandId.CopyName, 1),
        new MenuNode(MenuCommandId.Rename, 1),
        new MenuNode(MenuCommandId.CreateShortcut, 1),
        new MenuNode(MenuCommandId.Delete, 1),
        new MenuNode(MenuCommandId.Extract, 0),
        new MenuNode(MenuCommandId.NewSubmenu, 0),
        new MenuNode(MenuCommandId.NewFolder, 1),
        new MenuNode(MenuCommandId.Properties, 0),
    };

    /// <summary>The same set, flat, for anything that only needs membership.</summary>
    public static IReadOnlyList<MenuCommandId> Hideable { get; } =
        HideableTree.Select(node => node.Id).ToArray();


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


/// <summary>
/// One row of the settings dialog's list of Wander's own menu entries:
/// which entry, and how deep it sits in the menu.
/// </summary>
/// <param name="Id">The entry.</param>
/// <param name="Depth">0 for a top-level row, 1 for a row inside a submenu.</param>
public sealed record MenuNode(MenuCommandId Id, int Depth);
