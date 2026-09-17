using Wander.Core.Actions;
using Wander.Core.Localization;
using Wander.Core.Rename;
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
///   (create, terminal, path). View mode, sorting, refresh and undo are
///   window state, not folder verbs: they live in the toolbar's «Вид»
///   menu and on hotkeys.</item>
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
/// The header's "Actions" menu is a third shape from the same rules
/// (<see cref="MenuPlace.Header"/>): Wander's own verbs in a fixed order
/// under a caption that says what they are about, and no shell rows. What
/// does not apply is absent, as in the context menu; the one row shown
/// greyed is an action whose tool is missing, with a tooltip saying which,
/// because installing it is something the user can do about it. The two
/// menus stay one catalog - a row hidden in settings is hidden in both.
/// </para>
///
/// <para>
/// The layer above never has to think about dangling separators: every
/// group is emitted with its divider and <see cref="Normalize"/> collapses
/// whatever the hiding rules left behind.
/// </para>
/// </summary>
public static class ContextMenuBuilder {
    public const string CaptionFolderKey = "MenuCaptionFolder";
    public const string CaptionSelectionKey = "MenuCaptionSelection";

    /// <summary>
    /// Verbs whose entries act on the file rather than open it, *despite*
    /// publishing a name. Everything else in that category is caught by
    /// <see cref="PublishesNoVerb"/>, so this list stays short — and only
    /// holds verbs observed on a live system, never guessed ones.
    /// </summary>
    private static readonly HashSet<string> _fileOperationVerbs = new(StringComparer.OrdinalIgnoreCase) {
        "previousversions",
    };


    public static IReadOnlyList<MenuEntry> Build(
        ContextMenuTarget target,
        ContextMenuSettings settings,
        IReadOnlyList<ShellMenuEntry>? shellItems = null) {

        var shell = SplitShell(settings, shellItems);
        var raw = target.Place == MenuPlace.Header
            ? BuildHeader(target)
            : target.IsBackground
                ? BuildBackground(target, shell)
                : BuildSelection(target, shell);

        return Normalize(raw, settings);
    }


    // --- Menu shapes ----------------------------------------------------

    private static List<MenuEntry> BuildSelection(ContextMenuTarget t, ShellGroups shell) {
        if (t.IsArchive) {
            return BuildInsideArchive(t);
        }

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

        // The user's own actions, right under the third-party ones: the same
        // kind of thing, "do this to these files with that program". Only
        // what applies - a context menu does not explain itself.
        if (fs) {
            items.AddRange(ContextActionRows(t));
            items.Add(MenuEntry.Divider);
        }

        var fileGroup = new List<MenuEntry> {
            Cmd(MenuCommandId.Cut, fs),
            Cmd(MenuCommandId.Copy, fs),
            Cmd(MenuCommandId.Paste, fs && t.CanPaste),
            MenuEntry.Divider,
            Cmd(MenuCommandId.CopyPath),
            Cmd(MenuCommandId.CopyName),
            MenuEntry.Divider,
            Cmd(MenuCommandId.Rename, t.IsSingle && fs),
        };
        // Two or more of one kind: the window. Absent otherwise - on one
        // file F2 renames in place, and a mixed selection is refused
        // (BatchRenameGate) rather than offered greyed.
        if (fs && t.RenameKind is BatchRenameKind.Files or BatchRenameKind.Folders) {
            fileGroup.Add(Cmd(MenuCommandId.BatchRename));
        }
        fileGroup.AddRange(new[] {
            Cmd(MenuCommandId.CreateShortcut, fs),
            MenuEntry.Divider,
            Cmd(MenuCommandId.Delete, fs),
        });
        if (shell.FileOperations.Count > 0) {
            fileGroup.Add(MenuEntry.Divider);
            fileGroup.AddRange(shell.FileOperations);
        }
        items.Add(Sub(MenuCommandId.FileSubmenu, fileGroup));

        // An archive sitting in an ordinary folder: everything above still
        // applies to it as a file, and this is the one verb it has as a
        // container. The shell's own "Извлечь все..." arrives among the
        // third-party rows above and is left where it is - it is somebody
        // else's verb and it behaves differently.
        if (t.SelectionIsArchive) {
            items.Add(MenuEntry.Divider);
            items.Add(Cmd(MenuCommandId.Extract));
        }

        items.Add(MenuEntry.Divider);
        items.Add(Cmd(MenuCommandId.Properties, t.IsSingle));

        return items;
    }

    /// <summary>
    /// The menu on a row inside an archive. Four verbs, and nothing that
    /// would write: the container is read-only by decision, so rename,
    /// delete, cut and paste are not greyed out here - they are simply not
    /// things this place offers. Third-party rows are absent for the same
    /// reason the Recycle Bin has none: the shell is never queried in a
    /// read-only location.
    /// </summary>
    private static List<MenuEntry> BuildInsideArchive(ContextMenuTarget t) {
        return new List<MenuEntry> {
            // Open is single-item here for the same reason as everywhere
            // else: it opens the row under the cursor, not a selection.
            Cmd(MenuCommandId.Open, t.IsSingle, isDefault: true),
            MenuEntry.Divider,
            Cmd(MenuCommandId.Copy),
            Cmd(MenuCommandId.Extract),
            Cmd(MenuCommandId.CopyPath),
        };
    }

    private static List<MenuEntry> BuildBackground(ContextMenuTarget t, ShellGroups shell) {
        if (t.IsArchive) {
            // Nothing is created, opened in a terminal or pasted here; the
            // path is the one thing a click on empty space can still give.
            return new List<MenuEntry> { Cmd(MenuCommandId.CopyPath) };
        }

        bool fs = t.IsWritable;

        var items = new List<MenuEntry> {
            // One "Создать", not two: Windows contributes its own — folder,
            // shortcut, and every registered file template — and it lands
            // here rather than beside us. Ours leads with the folder row,
            // because that one goes through Wander's undo and its inline
            // rename; the shell's copy of it was dropped in SplitShell.
            Sub(MenuCommandId.NewSubmenu,
                new[] { Cmd(MenuCommandId.NewFolder, fs) }.Concat(shell.New).ToArray()),
            MenuEntry.Divider,

            Cmd(MenuCommandId.OpenInTerminal, fs),
            Cmd(MenuCommandId.CopyPath),
            MenuEntry.Divider,
        };

        // Actions for folders act on the folder being listed.
        if (fs) {
            items.AddRange(ContextActionRows(t));
            items.Add(MenuEntry.Divider);
        }

        // No File submenu here, so the folder's own shell verbs stay inline
        // rather than inventing a one-item container for them.
        items.AddRange(shell.TopLevel);
        items.AddRange(shell.FileOperations);

        items.Add(MenuEntry.Divider);
        items.Add(Cmd(MenuCommandId.Properties));

        return items;
    }

    /// <summary>
    /// The header's "Actions". The rows keep their order, and a caption on
    /// top says what they are about; a row that cannot act on this
    /// selection is left out rather than greyed.
    /// </summary>
    private static List<MenuEntry> BuildHeader(ContextMenuTarget t) {
        bool fs = t.IsWritable;
        bool any = t.Selection.Count > 0;

        var items = new List<MenuEntry> { Caption(t), MenuEntry.Divider };
        if (fs && t.RenameKind is BatchRenameKind.Files or BatchRenameKind.Folders) {
            items.Add(Cmd(MenuCommandId.BatchRename));
        }
        items.AddRange(HeaderActionRows(t));
        items.Add(MenuEntry.Divider);

        // The folder verbs the context menu keeps inside "File": up here
        // they are what the menu is for. With nothing selected, the ones
        // about a folder work on the folder on screen.
        if (any && (t.SelectionIsArchive || t.IsArchive)) {
            items.Add(Cmd(MenuCommandId.Extract));
        }
        if (fs && any) {
            items.Add(Cmd(MenuCommandId.CreateShortcut));
        }
        items.Add(Cmd(MenuCommandId.CopyPath));
        if (fs && (!any || (t.IsSingle && t.AllFolders))) {
            items.Add(Cmd(MenuCommandId.OpenInTerminal));
        }

        return items;
    }

    /// <summary>"4 изображения", "3 элемента", or the folder when nothing is selected.</summary>
    private static MenuEntry Caption(ContextMenuTarget t) {
        string header = t.Selection.Count == 0
            ? Text.Format(CaptionFolderKey, t.FolderPath ?? string.Empty)
            : Text.Plural(CaptionKey(FileTypeGroups.Classify(t.Selection)), t.Selection.Count);

        return new MenuEntry { Header = header, IsEnabled = false };
    }

    /// <summary>Resource key of the counted caption: the group's noun in its three forms.</summary>
    private static string CaptionKey(FileTypeGroup? group) {
        return group switch {
            FileTypeGroup.Images => "MenuCaptionImages",
            FileTypeGroup.Video => "MenuCaptionVideo",
            FileTypeGroup.Audio => "MenuCaptionAudio",
            FileTypeGroup.TextAndCode => "MenuCaptionTextAndCode",
            FileTypeGroup.Documents => "MenuCaptionDocuments",
            FileTypeGroup.Archives => "MenuCaptionArchives",
            FileTypeGroup.Folders => "MenuCaptionFolders",
            _ => CaptionSelectionKey,
        };
    }


    // --- Custom actions ------------------------------------------------

    /// <summary>
    /// What a right-click shows of the catalog: the rows that apply, and
    /// nothing else. Loose rows first, then the two submenus; an empty
    /// submenu drops out in <see cref="Normalize"/>.
    /// </summary>
    private static List<MenuEntry> ContextActionRows(ContextMenuTarget t) {
        var rows = t.Actions
            .Where(a => a.Enabled && a.Placement != ActionPlacement.Header)
            .Select(a => (Action: a, State: StateOf(a, t)))
            .Where(r => r.State == ActionState.Applicable)
            .ToList();

        var items = rows.Where(r => !r.Action.InSubmenu)
            .Select(r => ActionRow(r.Action, r.State))
            .ToList();
        foreach (var category in new[] { ActionCategory.Actions, ActionCategory.Convert }) {
            var inside = rows.Where(r => r.Action.InSubmenu && r.Action.Category == category).ToList();
            var submenu = inside.Select(r => ActionRow(r.Action, r.State)).ToList();
            AppendToFolder(submenu, inside);
            AddSubmenu(items, category == ActionCategory.Actions ? MenuCommandId.ActionsSubmenu : MenuCommandId.ConvertSubmenu, submenu);
        }

        return items;
    }

    /// <summary>
    /// The header's view of the catalog: the actions that apply, plus the
    /// ones that would if their tool were installed (greyed, saying which),
    /// and a way to the settings page at the bottom of each submenu. The
    /// user's own submenu is always there, with a placeholder when nothing
    /// in it applies; "Convert" only when some preset does. Nothing is
    /// offered where files cannot be written.
    /// </summary>
    private static List<MenuEntry> HeaderActionRows(ContextMenuTarget t) {
        var rows = t.Actions
            .Where(a => t.IsWritable && a.Enabled && a.Placement != ActionPlacement.ContextMenu)
            .Select(a => (Action: a, State: StateOf(a, t)))
            .Where(r => r.State is ActionState.Applicable or ActionState.ToolMissing)
            .ToList();

        var items = rows.Where(r => !r.Action.InSubmenu)
            .Select(r => ActionRow(r.Action, r.State))
            .ToList();

        var ownRows = rows.Where(r => r.Action.InSubmenu && r.Action.Category == ActionCategory.Actions).ToList();
        var actions = ownRows.Select(r => ActionRow(r.Action, r.State)).ToList();
        if (actions.Count == 0) {
            actions.Add(Cmd(MenuCommandId.NoActions, enabled: false));
        }
        AppendToFolder(actions, ownRows);
        actions.Add(MenuEntry.Divider);
        actions.Add(Cmd(MenuCommandId.ConfigureActions));
        AddSubmenu(items, MenuCommandId.ActionsSubmenu, actions);

        var presetRows = rows.Where(r => r.Action.InSubmenu && r.Action.Category == ActionCategory.Convert).ToList();
        var convert = presetRows.Select(r => ActionRow(r.Action, r.State)).ToList();
        if (convert.Count > 0) {
            AppendToFolder(convert, presetRows);
            convert.Add(MenuEntry.Divider);
            convert.Add(Cmd(MenuCommandId.ConfigureActions));
        }
        AddSubmenu(items, MenuCommandId.ConvertSubmenu, convert);

        return items;
    }

    /// <summary>
    /// "В другую папку ▸" at the end of a submenu: the same actions, for
    /// the ones that declare an output and apply, with the output going to
    /// a folder the user is asked for. An action without a declared output
    /// has nothing to redirect and stays out.
    /// </summary>
    private static void AppendToFolder(List<MenuEntry> submenu, IReadOnlyList<(CustomAction Action, ActionState State)> rows) {
        var redirectable = rows
            .Where(r => r.State == ActionState.Applicable && r.Action.Output.Length > 0)
            .Select(r => ActionRow(r.Action, r.State, toFolder: true))
            .ToList();
        if (redirectable.Count == 0) {
            return;
        }

        submenu.Add(MenuEntry.Divider);
        submenu.Add(Sub(MenuCommandId.ToFolderSubmenu, redirectable));
    }

    /// <summary>
    /// A submenu only when it has rows. <see cref="Normalize"/> drops an
    /// empty header as well; not making one keeps the raw list honest.
    /// </summary>
    private static void AddSubmenu(List<MenuEntry> items, MenuCommandId id, IReadOnlyList<MenuEntry> children) {
        if (children.Count > 0) {
            items.Add(Sub(id, children));
        }
    }

    private static ActionState StateOf(CustomAction action, ContextMenuTarget t) {
        return ActionApplicability.For(action, t.Selection, t.FolderPath is not null && t.IsWritable, t.ToolAvailable);
    }

    /// <param name="toFolder">The row that asks for a folder first (<see cref="MenuCommandId.RunActionTo"/>).</param>
    internal static MenuEntry ActionRow(CustomAction action, ActionState state, bool toFolder = false) {
        bool missing = state == ActionState.ToolMissing;

        return new MenuEntry {
            Id = toFolder ? MenuCommandId.RunActionTo : MenuCommandId.RunAction,
            Header = action.DisplayTitle,
            Argument = action.Id,
            IsEnabled = state == ActionState.Applicable,
            Tooltip = missing ? Text.Format(ActionApplicability.ToolMissingKey, action.RequiredTool) : null,
            IconPath = action.Kind == ActionKind.Command && Path.IsPathRooted(action.Program) ? action.Program : null,
        };
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
        IReadOnlyList<MenuEntry> shellNew = Array.Empty<MenuEntry>();

        foreach (var item in shellItems) {
            if (item.IsSeparator) {
                top.Add(MenuEntry.Divider);
                continue;
            }
            // Blocking is top-level only: the user blocks "7-Zip", not each
            // of the fourteen verbs inside it.
            if (settings.IsBlocked(item.Verb, item.Header)) {
                continue;
            }

            // The shell's own "Open with" popup is not shown as a sibling —
            // its contents are poured into Wander's Открыть submenu, which
            // is the whole point of having that submenu.
            if (IsOpenWithPopup(item)) {
                openWith = item.Children.Select(ConvertShellEntry).ToArray();
                continue;
            }

            // Same treatment for the shell's own "Создать": its contents go
            // into Wander's submenu, minus the folder row Wander already has.
            if (IsNewPopup(item)) {
                shellNew = item.Children
                    .Where(child => !IsShellNewFolder(child))
                    .Select(ConvertShellEntry)
                    .ToArray();
                continue;
            }

            var converted = ConvertShellEntry(item);
            if (IsFileOperation(item)) {
                fileOps.Add(converted);
            } else {
                top.Add(converted);
            }
        }

        return new ShellGroups(
            TrimSeparators(top), TrimSeparators(fileOps),
            TrimSeparators(openWith), TrimSeparators(shellNew));
    }

    private static bool IsOpenWithPopup(ShellMenuEntry item) {
        return item.HasChildren && string.Equals(item.Verb, "openas", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The shell's "Создать" popup. It publishes no verb of its own, so it
    /// is recognised by what its children publish: the two fixed rows carry
    /// "NewFolder" / "NewLink" and every template carries its extension.
    /// Those are canonical and untranslated — matching the header text would
    /// have broken on the first non-Russian machine.
    /// </summary>
    private static bool IsNewPopup(ShellMenuEntry item) {
        return item.HasChildren
            && item.Verb.Length == 0
            && item.Children.Any(IsShellNewFolder);
    }

    private static bool IsShellNewFolder(ShellMenuEntry item) {
        return string.Equals(item.Verb, "NewFolder", StringComparison.OrdinalIgnoreCase);
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
        IReadOnlyList<MenuEntry> OpenWith,
        IReadOnlyList<MenuEntry> New) {

        public static readonly ShellGroups Empty = new(
            Array.Empty<MenuEntry>(), Array.Empty<MenuEntry>(),
            Array.Empty<MenuEntry>(), Array.Empty<MenuEntry>());
    }


    // --- Normalisation ---------------------------------------------------

    /// <summary>
    /// Ids that only ever head a submenu. One of them with no rows is not a
    /// leaf to click but an empty submenu, and goes the same way.
    /// </summary>
    private static readonly HashSet<MenuCommandId> _submenuHeaders = new() {
        MenuCommandId.OpenSubmenu,
        MenuCommandId.FileSubmenu,
        MenuCommandId.NewSubmenu,
        MenuCommandId.ActionsSubmenu,
        MenuCommandId.ConvertSubmenu,
        MenuCommandId.ToFolderSubmenu,
    };


    /// <summary>
    /// Drops what the user hid, drops submenus that are empty - built so,
    /// or left so by the hiding - and collapses the separators the removals
    /// stranded.
    /// </summary>
    internal static IReadOnlyList<MenuEntry> Normalize(IEnumerable<MenuEntry> items, ContextMenuSettings settings) {
        var kept = new List<MenuEntry>();

        foreach (var item in items) {
            if (item.IsSeparator) {
                kept.Add(item);
                continue;
            }
            if (settings.IsHidden(item.Id)) {
                continue;
            }

            if (item.HasChildren || _submenuHeaders.Contains(item.Id)) {
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

    internal static MenuEntry Sub(MenuCommandId id, IReadOnlyList<MenuEntry> children) {
        return new MenuEntry {
            Id = id,
            Header = ContextMenuCatalog.Title(id),
            Children = children,
        };
    }
}
