using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Rename;

namespace Wander.Core.Menu;

/// <summary>Which menu is being built - the shape differs, the rules do not.</summary>
public enum MenuPlace {
    /// <summary>A right-click: what applies is shown, the rest is absent.</summary>
    Context,

    /// <summary>The header's "Actions": a fixed order and a caption; only a missing tool is greyed and explained.</summary>
    Header,
}


/// <summary>
/// Everything the menu is allowed to know about the right-click that
/// produced it. Passing a snapshot (rather than the ViewModel) is what
/// makes <see cref="ContextMenuBuilder"/> a pure function and therefore
/// testable without a UI.
/// </summary>
public sealed record ContextMenuTarget {
    public MenuPlace Place { get; init; } = MenuPlace.Context;

    /// <summary>Items under the cursor. Empty for a background click.</summary>
    public IReadOnlyList<FileSystemEntry> Selection { get; init; } = Array.Empty<FileSystemEntry>();

    /// <summary>The actions catalog, presets included; the builder keeps the enabled ones.</summary>
    public IReadOnlyList<CustomAction> Actions { get; init; } = Array.Empty<CustomAction>();

    /// <summary>
    /// Tools (<see cref="CustomAction.RequiredTool"/>) the machine does not
    /// have. Detection is the app's business and happens once; the menu
    /// only reads the answer.
    /// </summary>
    public IReadOnlySet<string> MissingTools { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folder currently listed. Null only before the first navigation.</summary>
    public string? FolderPath { get; init; }

    /// <summary>True when the user right-clicked empty space rather than a row.</summary>
    public bool IsBackground { get; init; }

    /// <summary>
    /// True inside a shell namespace (the Recycle Bin today). Entries there
    /// are backed by <c>$Recycle.Bin</c> files, so every filesystem verb is
    /// suppressed — same reason the commands themselves refuse to run.
    /// </summary>
    public bool IsReadOnlyLocation { get; init; }

    public bool CanPaste { get; init; }


    /// <summary>Exactly one item under the cursor — the precondition for Rename / Properties.</summary>
    public bool IsSingle => Selection.Count == 1;

    /// <summary>At least one selected item is a folder — blocks "Open with".</summary>
    public bool AnyFolder => Selection.Any(e => e.IsFolderLike);

    /// <summary>
    /// Every selected item is a real directory. Shortcuts pointing at one
    /// don't count: "open in terminal" would have to resolve the link
    /// first, and nothing here does that.
    /// </summary>
    public bool AllFolders => Selection.Count > 0 && Selection.All(e => e.Kind == EntryKind.Directory);

    /// <summary>Shorthand for "real filesystem verbs are allowed here".</summary>
    public bool IsWritable => !IsReadOnlyLocation;

    /// <summary>Whether the selection may go to the batch-rename window, and why not.</summary>
    public BatchRenameKind RenameKind => BatchRenameGate.Classify(Selection);

    public bool ToolAvailable(string tool) => tool.Length == 0 || !MissingTools.Contains(tool);

    /// <summary>
    /// The listing is the Recycle Bin. Read-only like any shell namespace,
    /// but with one thing you *can* do to its contents — put them back.
    /// </summary>
    public bool IsRecycleBin { get; init; }

    /// <summary>
    /// The listing is inside an archive. Read-only like the bin, and with
    /// its own short menu: what you can do to a thing in a container you
    /// cannot write to is open it, copy it, or take it out.
    /// </summary>
    public bool IsArchive { get; init; }

    /// <summary>
    /// Every selected item is an archive Wander can open — in an ordinary
    /// folder, which is what earns the row its "Извлечь…". Computed by the
    /// caller: which extensions count is a property of the machine, and
    /// this record stays a plain snapshot.
    /// </summary>
    public bool SelectionIsArchive { get; init; }
}
