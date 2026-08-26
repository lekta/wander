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
/// Vertical order follows frequency, not category. What the user opened the
/// menu *for* — "edit this photo in ...", "extract here", "commit" — is at
/// the top where the cursor already is; Wander's own file operations are
/// the rare half and wait at the bottom inside one "Файл" submenu.
/// </para>
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

        var shell = SplitShell(settings, shellItems);
        var raw = target.IsBackground
            ? BuildBackground(target, shell)
            : BuildSelection(target, shell);

        return Normalize(raw, settings);
    }


    /// <summary>
    /// Verbs whose entries act on the file rather than open it, *despite*
    /// publishing a name. Everything else in that category is caught by
    /// <see cref="PublishesNoVerb"/>, so this list stays short — and only
    /// holds verbs observed on a live system, never guessed ones.
    /// </summary>
    private static readonly HashSet<string> _fileOperationVerbs = new(StringComparer.OrdinalIgnoreCase) {
        "previousversions",
    };


    // --- Menu shapes ----------------------------------------------------

    private static List<MenuEntry> BuildSelection(ContextMenuTarget t, ShellGroups shell) {
        bool fs = t.IsWritable;
        // The shell's own list of apps is richer than anything we could
        // assemble; ours is the fallback for when it isn't offered. Neither
        // applies to a folder, and an empty submenu drops out on its own.
        var openWith = shell.OpenWith.Count > 0
            ? shell.OpenWith
            : t.IsSingle && !t.AnyFolder && fs
                ? new[] { Cmd(MenuCommandId.OpenWith) }
                : Array.Empty<MenuEntry>();

        var items = new List<MenuEntry>();

        // In the bin, restoring is the reason the menu was opened at all, so
        // it goes first and is the default action.
        if (t.IsRecycleBin) {
            items.Add(Cmd(MenuCommandId.RestoreFromRecycleBin, isDefault: true));
            items.Add(MenuEntry.Divider);
        }

        items.Add(Cmd(MenuCommandId.Open, t.IsSingle, isDefault: !t.IsRecycleBin));

        if (openWith.Count > 0) {
            items.Add(Sub(MenuCommandId.OpenSubmenu, openWith));
        }

        // Only meaningful for a folder: a terminal opened "on" a file would
        // silently land in the folder it happens to sit in, which is not
        // what the row says. So it is dropped, not greyed.
        if (t.IsSingle && t.AllFolders && fs) {
            items.Add(Cmd(MenuCommandId.OpenInTerminal));
        }
        items.Add(MenuEntry.Divider);

        // Third-party verbs sit where the eye lands first: for a photo,
        // "edit in ..." is what the menu was opened for. Wander's own file
        // operations are rarer and wait at the bottom.
        items.AddRange(shell.TopLevel);
        items.Add(MenuEntry.Divider);

        var fileGroup = new List<MenuEntry> {
            Cmd(MenuCommandId.Cut, fs),
            Cmd(MenuCommandId.Copy, fs),
            Cmd(MenuCommandId.Paste, fs && t.CanPaste),
            MenuEntry.Divider,
            Cmd(MenuCommandId.CopyPath),
            Cmd(MenuCommandId.CopyName),
            MenuEntry.Divider,
            Cmd(MenuCommandId.Rename, t.IsSingle && fs),
            Cmd(MenuCommandId.CreateShortcut, fs),
            MenuEntry.Divider,
            Cmd(MenuCommandId.Delete, fs),
        };
        if (shell.FileOperations.Count > 0) {
            fileGroup.Add(MenuEntry.Divider);
            fileGroup.AddRange(shell.FileOperations);
        }
        items.Add(Sub(MenuCommandId.FileSubmenu, fileGroup));

        items.Add(MenuEntry.Divider);
        items.Add(Cmd(MenuCommandId.Properties, t.IsSingle));

        return items;
    }

    private static List<MenuEntry> BuildBackground(ContextMenuTarget t, ShellGroups shell) {
        bool fs = t.IsWritable;

        var items = new List<MenuEntry> {
            // Paste and New folder are what a background right-click is for
            // nine times out of ten, so they get the top slot rather than
            // sitting inside the File submenu the selection menu uses.
            Cmd(MenuCommandId.Paste, fs && t.CanPaste),
            Cmd(MenuCommandId.NewFolder, fs),
            MenuEntry.Divider,

            Sub(MenuCommandId.ViewSubmenu, new[] {
                Check(MenuCommandId.ViewDetails, t.ViewMode == "Details"),
                Check(MenuCommandId.ViewTiles, t.ViewMode == "Tiles"),
                Check(MenuCommandId.ViewLargeIcons, t.ViewMode == "LargeIcons"),
                MenuEntry.Divider,
                Check(MenuCommandId.TogglePreview, t.IsPreviewVisible),
            }),

            Sub(MenuCommandId.SortSubmenu, new[] {
                Check(MenuCommandId.SortByName, t.SortKey == SortKey.Name),
                Check(MenuCommandId.SortByDate, t.SortKey == SortKey.ModifiedDate),
                Check(MenuCommandId.SortBySize, t.SortKey == SortKey.Size),
                Check(MenuCommandId.SortByType, t.SortKey == SortKey.Type),
                MenuEntry.Divider,
                Check(MenuCommandId.SortAscending, t.SortAscending),
                Check(MenuCommandId.SortFoldersFirst, t.GroupFoldersFirst),
            }),

            Cmd(MenuCommandId.Refresh),
            Cmd(MenuCommandId.Undo, t.CanUndo),
            MenuEntry.Divider,

            Cmd(MenuCommandId.OpenInTerminal, fs),
            Cmd(MenuCommandId.CopyPath),
            MenuEntry.Divider,
        };

        // No File submenu here, so the folder's own shell verbs stay inline
        // rather than inventing a one-item container for them.
        items.AddRange(shell.TopLevel);
        items.AddRange(shell.FileOperations);

        items.Add(MenuEntry.Divider);
        items.Add(Cmd(MenuCommandId.Properties));

        return items;
    }

    // --- Shell extensions -----------------------------------------------

    /// <summary>
    /// Sorts what the shell reported into three piles, because they belong
    /// in three different places.
    ///
    /// <para>
    /// Classification runs on the canonical verb, never on the label: labels
    /// are localised and change with the file name ("Добавить к \"README.7z\""),
    /// verbs do not. Where no verb is published at all, that absence is
    /// itself the signal — see <see cref="PublishesNoVerb"/>.
    /// </para>
    /// </summary>
    private static ShellGroups SplitShell(
        ContextMenuSettings settings,
        IReadOnlyList<ShellMenuEntry>? shellItems) {

        if (!settings.ShellExtensionsEnabled || shellItems is null || shellItems.Count == 0) {
            return ShellGroups.Empty;
        }

        var top = new List<MenuEntry>();
        var fileOps = new List<MenuEntry>();
        IReadOnlyList<MenuEntry> openWith = Array.Empty<MenuEntry>();

        foreach (var item in shellItems) {
            if (item.IsSeparator) {
                top.Add(MenuEntry.Divider);
                continue;
            }
            // Blocking is top-level only: the user blocks "7-Zip", not each
            // of the fourteen verbs inside it.
            if (settings.IsBlocked(item.Header)) {
                continue;
            }

            // The shell's own "Open with" popup is not shown as a sibling —
            // its contents are poured into Wander's Открыть submenu, which
            // is the whole point of having that submenu.
            if (IsOpenWithPopup(item)) {
                openWith = item.Children.Select(ConvertShellEntry).ToArray();
                continue;
            }

            var converted = ConvertShellEntry(item);
            if (IsFileOperation(item)) {
                fileOps.Add(converted);
            } else {
                top.Add(converted);
            }
        }

        return new ShellGroups(TrimSeparators(top), TrimSeparators(fileOps), TrimSeparators(openWith));
    }

    private static bool IsOpenWithPopup(ShellMenuEntry item) {
        return item.HasChildren && string.Equals(item.Verb, "openas", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True for entries that act *on* the file rather than open it — those
    /// belong at the bottom, next to Wander's own file verbs.
    /// </summary>
    private static bool IsFileOperation(ShellMenuEntry item) {
        return _fileOperationVerbs.Contains(item.Verb) || PublishesNoVerb(item);
    }

    /// <summary>
    /// Windows' own plumbing publishes no canonical verb: "Отправить" and
    /// "Передать на устройство" are assembled at popup time from a folder
    /// listing and a device scan, and "Проверка с использованием Microsoft
    /// Defender" simply doesn't register one. Handlers that exist to *open*
    /// a file — Notepad++, 7-Zip, TortoiseGit, the Photos editors — always
    /// do. So "publishes nothing we can name" is what separates the two
    /// without matching localised labels.
    ///
    /// <para>
    /// A heuristic, not a contract: a third-party handler that skips verbs
    /// would be filed under file operations too. That costs it a place in
    /// the menu, not correctness — and the user can hide it either way.
    /// </para>
    /// </summary>
    private static bool PublishesNoVerb(ShellMenuEntry item) {
        if (item.HasChildren) {
            return item.Children.All(child => child.IsSeparator || child.Verb.Length == 0);
        }

        return item.Verb.Length == 0;
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


    /// <summary>Shell entries split by where they go in Wander's menu.</summary>
    private sealed record ShellGroups(
        IReadOnlyList<MenuEntry> TopLevel,
        IReadOnlyList<MenuEntry> FileOperations,
        IReadOnlyList<MenuEntry> OpenWith) {

        public static readonly ShellGroups Empty = new(
            Array.Empty<MenuEntry>(), Array.Empty<MenuEntry>(), Array.Empty<MenuEntry>());
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

    private static MenuEntry Sub(MenuCommandId id, IReadOnlyList<MenuEntry> children) {
        return new MenuEntry {
            Id = id,
            Header = ContextMenuCatalog.Title(id),
            Children = children,
        };
    }
}
