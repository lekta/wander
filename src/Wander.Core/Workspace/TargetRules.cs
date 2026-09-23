using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Panels;

namespace Wander.Core.Workspace;

/// <summary>
/// Everything the target is worked out from (REDESIGN 4.3). The window
/// reports where the keyboard is, the list what it has selected, each panel
/// where its cursor stands, the window which menu is open; none of them
/// decides what the next operation is about.
/// </summary>
public sealed record TargetFacts {
    /// <summary>The zone the keyboard is in, or null when it is in none - on the window, a menu, the preview.</summary>
    public WindowZone? Zone { get; init; }

    /// <summary>The last zone the keyboard was in: what a keyboard in no zone still means.</summary>
    public WindowZone LastZone { get; init; } = WindowZone.FileList;

    /// <summary>The list's selection, whether the keyboard is there or not.</summary>
    public IReadOnlyList<FileSystemEntry> ListSelection { get; init; } = Array.Empty<FileSystemEntry>();

    /// <summary>The list's current row among the selection.</summary>
    public FileSystemEntry? ListPrimary { get; init; }

    /// <summary>The row under the bookmarks panel's cursor.</summary>
    public string? BookmarksCaret { get; init; }

    /// <summary>The row under the drives panel's cursor.</summary>
    public string? DrivesCaret { get; init; }

    /// <summary>What the open context menu is about, while one is open.</summary>
    public Target? MenuSubject { get; init; }
}


/// <summary>How F2 and "Rename" are carried out on a target.</summary>
public enum RenameRoute {
    /// <summary>Nothing to rename.</summary>
    None,

    /// <summary>The editor over the one row of the list.</summary>
    ListRow,

    /// <summary>Two rows or more: the batch window.</summary>
    ListBatch,

    /// <summary>The editor over the row of a panel.</summary>
    PanelRow,
}


/// <summary>What Enter and "Open" do with a target.</summary>
public enum OpenRoute {
    /// <summary>Nothing to open.</summary>
    None,

    /// <summary>Open the list's current row: a file with its program, a folder by walking into it.</summary>
    ListRow,

    /// <summary>Go to the panel row's folder.</summary>
    PanelRow,
}


/// <summary>
/// The target and what each command does with it (REDESIGN 4.3). The
/// target is a function of where the keyboard is, never an effect of it
/// arriving somewhere: the keyboard coming into a panel does not take the
/// list's selection away, and leaving a panel leaves nothing of it behind.
/// A command from a menu asks about the menu's subject; one from a key asks
/// about the target as it is at that moment.
/// </summary>
public static class TargetRules {
    /// <summary>
    /// The target. An open menu is about its subject; a keyboard in a panel
    /// is about the row under that panel's cursor; anywhere else - the list,
    /// the address bar, the filter, the toolbar - it is about the list's
    /// selection. A keyboard in no zone means what it meant in the last one.
    /// </summary>
    public static Target Of(TargetFacts facts) {
        if (facts.MenuSubject is { } subject) {
            return subject;
        }

        return (facts.Zone ?? facts.LastZone) switch {
            WindowZone.Bookmarks => Target.OfPanelRow(Pane.Bookmarks, facts.BookmarksCaret),
            WindowZone.Drives => Target.OfPanelRow(Pane.Drives, facts.DrivesCaret),
            _ => Target.OfRows(facts.ListSelection, facts.ListPrimary),
        };
    }

    /// <summary>
    /// The facts as the model holds them - the keyboard, both cursors, the
    /// open menu. The list's rows are the caller's to add while the list
    /// keeps its own selection (until block 2, step 7).
    /// </summary>
    public static TargetFacts FactsOf(WorkspaceState state) {
        return new TargetFacts {
            Zone = state.Keyboard.Zone,
            LastZone = state.Keyboard.LastZone,
            BookmarksCaret = state.Bookmarks.Caret,
            DrivesCaret = state.Drives.Caret,
            MenuSubject = state.Menu?.Subject,
        };
    }


    /// <summary>
    /// The items Delete, Cut, Copy and the actions work on: the rows, or the
    /// panel row's folder standing in as one. Empty for no target and for a
    /// background - there is nothing there to delete.
    /// </summary>
    public static IReadOnlyList<FileSystemEntry> Items(Target target) {
        return target.Kind switch {
            TargetKind.ListRows => target.Rows,
            TargetKind.PanelRow => new[] { FolderEntry(target.Folder!) },
            _ => Array.Empty<FileSystemEntry>(),
        };
    }


    /// <summary>
    /// Where a paste goes. A panel row or a background is the folder itself;
    /// from a menu, the one folder selected in the list is (Explorer's row
    /// menu); everything else - Ctrl+V in the list with a folder selected
    /// among the rows included - goes into the open folder.
    /// </summary>
    public static string? PasteFolder(Target target, string? openFolder, bool fromMenu) {
        return target.Kind switch {
            TargetKind.PanelRow or TargetKind.Background => target.Folder,
            TargetKind.ListRows when fromMenu && target.Rows is [{ Kind: EntryKind.Directory } folder] => folder.FullPath,
            _ => openFolder,
        };
    }


    /// <summary>What Alt+Enter and "Properties" show: the one row, else the open folder.</summary>
    public static string? PropertiesOf(Target target, string? openFolder) {
        return target.Kind switch {
            TargetKind.ListRows => target.Primary!.FullPath,
            TargetKind.PanelRow or TargetKind.Background => target.Folder,
            _ => openFolder,
        };
    }


    /// <summary>
    /// Where a terminal opens: the one folder of the target, else the open
    /// folder - a terminal opened "on" a file would land in the folder it
    /// sits in anyway.
    /// </summary>
    public static string? TerminalFolder(Target target, string? openFolder) {
        return target.Kind switch {
            TargetKind.ListRows => target.Rows is [{ Kind: EntryKind.Directory } folder] ? folder.FullPath : openFolder,
            TargetKind.PanelRow or TargetKind.Background => target.Folder,
            _ => openFolder,
        };
    }


    /// <summary>What "Copy path" puts on the clipboard: the target's paths, else the open folder's.</summary>
    public static IReadOnlyList<string> CopyPaths(Target target, string? openFolder) {
        return target.Kind switch {
            TargetKind.ListRows => target.Rows.Select(r => r.FullPath).ToArray(),
            TargetKind.PanelRow or TargetKind.Background => new[] { target.Folder! },
            _ => openFolder is null ? Array.Empty<string>() : new[] { openFolder },
        };
    }


    /// <summary>How F2 renames the target: in place on the surface it is on, two rows or more in the window.</summary>
    public static RenameRoute Rename(Target target) {
        return target.Kind switch {
            TargetKind.ListRows => target.Rows.Count > 1 ? RenameRoute.ListBatch : RenameRoute.ListRow,
            TargetKind.PanelRow => RenameRoute.PanelRow,
            _ => RenameRoute.None,
        };
    }


    /// <summary>What Enter opens: the list's row, or the panel row's folder to go to.</summary>
    public static OpenRoute Open(Target target) {
        return target.Kind switch {
            TargetKind.ListRows => OpenRoute.ListRow,
            TargetKind.PanelRow => OpenRoute.PanelRow,
            _ => OpenRoute.None,
        };
    }


    /// <summary>
    /// A folder as a row, from its path alone: what a panel row is to the
    /// menu, the status bar and the actions, none of which needs more than
    /// "a folder called this, here". Nothing is read from the disk.
    /// </summary>
    public static FileSystemEntry FolderEntry(string path) {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);

        return new FileSystemEntry(
            name.Length > 0 ? name : path, path, EntryKind.Directory,
            Size: null, ModifiedUtc: default, IsHidden: false, IsReadOnly: false, IsSystem: false, LinksToDirectory: false);
    }
}
