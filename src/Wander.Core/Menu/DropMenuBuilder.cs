using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Localization;

namespace Wander.Core.Menu;

/// <summary>What a right-button drop has in hand, and where it landed.</summary>
public sealed record DropMenuTarget {
    /// <summary>
    /// The items the user dragged, companions folded away: what an action
    /// runs over and what the caption counts. The copy and the move take
    /// the whole payload, companions included - the app binds those rows
    /// to the drop itself.
    /// </summary>
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The dropped items as entries, for the actions' type rules. Looked up
    /// by the app; shorter than <see cref="Paths"/> when something could
    /// not be read, and then no action is offered - an action over half a
    /// drop is not what was asked for.
    /// </summary>
    public IReadOnlyList<FileSystemEntry> Entries { get; init; } = Array.Empty<FileSystemEntry>();

    public string TargetFolder { get; init; } = string.Empty;

    /// <summary>What a left-button drop would have done - the row drawn bold.</summary>
    public bool MoveByDefault { get; init; }

    /// <summary>The items come out of an archive: copying them out is all that can happen to them.</summary>
    public bool FromArchive { get; init; }

    public IReadOnlyList<CustomAction> Actions { get; init; } = Array.Empty<CustomAction>();

    public IReadOnlySet<string> MissingTools { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The user's hidden rows: a submenu put away in the context menu stays away here.</summary>
    public ContextMenuSettings Settings { get; init; } = ContextMenuSettings.Default;
}


/// <summary>
/// The menu a drop with the right mouse button opens, the way Explorer's
/// does: what is in hand and where, then the things a drop can do - the
/// one a left-button drop would have done in bold - and, past them, the
/// catalog actions that declare an output, with the output going into the
/// folder dropped on instead of beside the sources. Pure function of the
/// target, so the shape has tests; the app binds the rows.
/// </summary>
public static class DropMenuBuilder {
    public static IReadOnlyList<MenuEntry> Build(DropMenuTarget t) {
        var items = new List<MenuEntry> {
            new() { Id = MenuCommandId.DropCaption, Header = Caption(t), IsEnabled = false },
            MenuEntry.Divider,
            Cmd(MenuCommandId.DropCopyHere, isDefault: t.FromArchive || !t.MoveByDefault),
        };
        if (!t.FromArchive) {
            items.Add(Cmd(MenuCommandId.DropMoveHere, isDefault: t.MoveByDefault));
            items.Add(Cmd(MenuCommandId.DropLinkHere));
        }

        items.Add(MenuEntry.Divider);
        items.AddRange(ActionRows(t));
        items.Add(MenuEntry.Divider);
        items.Add(Cmd(MenuCommandId.DropCancel));

        return ContextMenuBuilder.Normalize(items, t.Settings);
    }


    /// <summary>"«photo.jpg» в Photos", "12 элем. в Photos" - the plaque's own words.</summary>
    private static string Caption(DropMenuTarget t) {
        string what = t.Paths.Count == 1
            ? Text.Format("DragOneItem", NameOf(t.Paths[0]))
            : Text.Format("DragItems", t.Paths.Count);

        return what + " " + Text.Format("DragTarget", NameOf(t.TargetFolder));
    }

    /// <summary>
    /// The two action submenus of the context menu, reduced to what a drop
    /// can use: rows that apply to every dropped item and declare an output
    /// - there is nothing to send into the folder otherwise.
    /// </summary>
    private static List<MenuEntry> ActionRows(DropMenuTarget t) {
        var items = new List<MenuEntry>();
        if (t.FromArchive || t.Entries.Count == 0 || t.Entries.Count != t.Paths.Count) {
            return items;
        }

        var rows = t.Actions
            .Where(a => a.Enabled && a.Output.Length > 0 && a.Placement != ActionPlacement.Header)
            .Where(a => ActionApplicability.For(a, t.Entries, hasFolder: false, tool => !t.MissingTools.Contains(tool))
                == ActionState.Applicable)
            .ToList();
        foreach (var category in new[] { ActionCategory.Actions, ActionCategory.Convert }) {
            var submenu = rows
                .Where(a => a.Category == category)
                .Select(a => ContextMenuBuilder.ActionRow(a, ActionState.Applicable, toFolder: true))
                .ToList();
            if (submenu.Count > 0) {
                items.Add(ContextMenuBuilder.Sub(
                    category == ActionCategory.Actions ? MenuCommandId.ActionsSubmenu : MenuCommandId.ConvertSubmenu,
                    submenu));
            }
        }

        return items;
    }

    private static string NameOf(string path) {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return name.Length > 0 ? name : path;
    }

    private static MenuEntry Cmd(MenuCommandId id, bool isDefault = false) {
        return new MenuEntry { Id = id, Header = ContextMenuCatalog.Title(id), IsDefault = isDefault };
    }
}
