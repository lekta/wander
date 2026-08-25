using Wander.Core.FileSystem;
using Wander.Core.Shell;

namespace Wander.Core.Menu;

/// <summary>
/// Turns "what was right-clicked" into the list of rows to draw. Pure
/// function of <see cref="ContextMenuTarget"/> + <see cref="ContextMenuSettings"/>
/// + whatever the shell reported, which is the whole point: menu shape is
/// the part that keeps growing rules ("Rename only on a single item",
/// "nothing destructive in the Recycle Bin"), and rules that live in Core
/// are rules that have tests.
///
/// <para>
/// Two shapes, deliberately different rather than one menu with half its
/// rows greyed out:
/// </para>
/// <list type="bullet">
///   <item>Selection — verbs that act on the clicked items.</item>
///   <item>Background — verbs that act on the folder being listed
///   (paste, view, sort, refresh).</item>
/// </list>
///
/// <para>
/// The layer above never has to think about dangling separators: every
/// group is emitted with its divider and <see cref="Normalize"/> collapses
/// whatever the hiding rules left behind.
/// </para>
/// </summary>
public static class ContextMenuBuilder {
    public static IReadOnlyList<MenuEntry> Build(
        ContextMenuTarget target,
        ContextMenuSettings settings,
        IReadOnlyList<ShellMenuEntry>? shellItems = null) {

        var shell = ConvertShell(settings, shellItems);
        var raw = target.IsBackground
            ? BuildBackground(target, shell)
            : BuildSelection(target, shell);

        return Normalize(raw, settings);
    }


    // --- Menu shapes ----------------------------------------------------

    private static List<MenuEntry> BuildSelection(ContextMenuTarget t, IReadOnlyList<MenuEntry> shell) {
        bool fs = t.IsWritable;

        var items = new List<MenuEntry> {
            Cmd(MenuCommandId.Open, t.IsSingle, isDefault: true),
            Cmd(MenuCommandId.OpenWith, t.IsSingle && !t.AnyFolder && fs),
            MenuEntry.Divider,

            // Clipboard verbs live one level down on purpose: they are the
            // ones people reach for by hotkey, so top level is better spent
            // on the verbs that have none.
            Sub(MenuCommandId.FileSubmenu,
                Cmd(MenuCommandId.Cut, fs),
                Cmd(MenuCommandId.Copy, fs),
                Cmd(MenuCommandId.Paste, fs && t.CanPaste),
                MenuEntry.Divider,
                Cmd(MenuCommandId.CopyPath),
                Cmd(MenuCommandId.CopyName),
                MenuEntry.Divider,
                Cmd(MenuCommandId.CreateShortcut, fs)),
            MenuEntry.Divider,

            Cmd(MenuCommandId.Rename, t.IsSingle && fs),
            Cmd(MenuCommandId.Delete, fs),
            Cmd(MenuCommandId.PermanentDelete, fs),
            MenuEntry.Divider,

            Cmd(MenuCommandId.AddBookmark, t.IsSingle && t.AllFolders && fs),
            Cmd(MenuCommandId.OpenInExplorer),
            Cmd(MenuCommandId.OpenInTerminal, fs),
            MenuEntry.Divider,
        };

        items.AddRange(shell);
        items.Add(MenuEntry.Divider);
        items.Add(Cmd(MenuCommandId.Properties, t.IsSingle));

        return items;
    }

    private static List<MenuEntry> BuildBackground(ContextMenuTarget t, IReadOnlyList<MenuEntry> shell) {
        bool fs = t.IsWritable;

        var items = new List<MenuEntry> {
            // Paste and New folder are what a background right-click is for
            // nine times out of ten, so they get the top slot rather than
            // sitting inside the File submenu the selection menu uses.
            Cmd(MenuCommandId.Paste, fs && t.CanPaste),
            Cmd(MenuCommandId.NewFolder, fs),
            MenuEntry.Divider,

            Sub(MenuCommandId.ViewSubmenu,
                Check(MenuCommandId.ViewDetails, t.ViewMode == "Details"),
                Check(MenuCommandId.ViewTiles, t.ViewMode == "Tiles"),
                Check(MenuCommandId.ViewLargeIcons, t.ViewMode == "LargeIcons"),
                MenuEntry.Divider,
                Check(MenuCommandId.TogglePreview, t.IsPreviewVisible)),

            Sub(MenuCommandId.SortSubmenu,
                Check(MenuCommandId.SortByName, t.SortKey == SortKey.Name),
                Check(MenuCommandId.SortByDate, t.SortKey == SortKey.ModifiedDate),
                Check(MenuCommandId.SortBySize, t.SortKey == SortKey.Size),
                Check(MenuCommandId.SortByType, t.SortKey == SortKey.Type),
                MenuEntry.Divider,
                Check(MenuCommandId.SortAscending, t.SortAscending),
                Check(MenuCommandId.SortFoldersFirst, t.GroupFoldersFirst)),

            Cmd(MenuCommandId.Refresh),
            MenuEntry.Divider,

            Cmd(MenuCommandId.CopyPath),
            Cmd(MenuCommandId.AddBookmark, fs),
            Cmd(MenuCommandId.OpenInExplorer),
            Cmd(MenuCommandId.OpenInTerminal, fs),
            MenuEntry.Divider,

            Cmd(MenuCommandId.Undo, t.CanUndo),
            MenuEntry.Divider,
        };

        items.AddRange(shell);
        items.Add(MenuEntry.Divider);
        items.Add(Cmd(MenuCommandId.Properties));

        return items;
    }


    // --- Shell extensions -----------------------------------------------

    private static IReadOnlyList<MenuEntry> ConvertShell(
        ContextMenuSettings settings,
        IReadOnlyList<ShellMenuEntry>? shellItems) {

        if (!settings.ShellExtensionsEnabled || shellItems is null || shellItems.Count == 0) {
            return Array.Empty<MenuEntry>();
        }

        var converted = new List<MenuEntry>();
        foreach (var item in shellItems) {
            // Blocking is top-level only: the user blocks "7-Zip", not each
            // of the fourteen verbs inside it.
            if (!item.IsSeparator && settings.IsBlocked(item.Header)) {
                continue;
            }
            converted.Add(ConvertShellEntry(item));
        }

        var trimmed = TrimSeparators(converted);
        if (trimmed.Count == 0 || !settings.ShellExtensionsInSubmenu) {
            return trimmed;
        }

        return new[] {
            new MenuEntry {
                Id = MenuCommandId.ShellSubmenu,
                Header = ContextMenuCatalog.Title(MenuCommandId.ShellSubmenu),
                Children = trimmed,
            },
        };
    }

    private static MenuEntry ConvertShellEntry(ShellMenuEntry item) {
        return new MenuEntry {
            Header = item.Header,
            IsSeparator = item.IsSeparator,
            IsEnabled = item.IsEnabled,
            IconPng = item.IconPng,
            // A submenu header carries no command of its own; only leaves
            // get an invokable id.
            ShellCommand = item.HasChildren ? -1 : item.CommandId,
            Children = item.Children.Select(ConvertShellEntry).ToArray(),
        };
    }


    // --- Normalisation ---------------------------------------------------

    /// <summary>
    /// Drops what the user hid, drops submenus left empty by that, and
    /// collapses the separators the removals stranded.
    /// </summary>
    private static IReadOnlyList<MenuEntry> Normalize(IEnumerable<MenuEntry> items, ContextMenuSettings settings) {
        var kept = new List<MenuEntry>();

        foreach (var item in items) {
            if (item.IsSeparator) {
                kept.Add(item);
                continue;
            }
            if (settings.IsHidden(item.Id)) {
                continue;
            }

            if (item.HasChildren) {
                var children = Normalize(item.Children, settings);
                if (children.Count == 0) {
                    continue;
                }
                kept.Add(item with { Children = children });
                continue;
            }

            kept.Add(item);
        }

        return TrimSeparators(kept);
    }

    private static IReadOnlyList<MenuEntry> TrimSeparators(IReadOnlyList<MenuEntry> items) {
        var result = new List<MenuEntry>(items.Count);
        foreach (var item in items) {
            // Leading and consecutive separators are dropped as we go...
            if (item.IsSeparator && (result.Count == 0 || result[^1].IsSeparator)) {
                continue;
            }
            result.Add(item);
        }
        // ...and a trailing one at the very end.
        if (result.Count > 0 && result[^1].IsSeparator) {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }


    // --- Row factories ---------------------------------------------------

    private static MenuEntry Cmd(MenuCommandId id, bool enabled = true, bool isDefault = false) {
        return new MenuEntry {
            Id = id,
            Header = ContextMenuCatalog.Title(id),
            Gesture = ContextMenuCatalog.Gesture(id),
            IsEnabled = enabled,
            IsDefault = isDefault,
        };
    }

    private static MenuEntry Check(MenuCommandId id, bool isChecked) {
        return new MenuEntry {
            Id = id,
            Header = ContextMenuCatalog.Title(id),
            Gesture = ContextMenuCatalog.Gesture(id),
            IsCheckable = true,
            IsChecked = isChecked,
        };
    }

    private static MenuEntry Sub(MenuCommandId id, params MenuEntry[] children) {
        return new MenuEntry {
            Id = id,
            Header = ContextMenuCatalog.Title(id),
            Children = children,
        };
    }
}
