using Wander.Core.FileSystem;

namespace Wander.Core.Menu;

/// <summary>
/// Everything the menu is allowed to know about the right-click that
/// produced it. Passing a snapshot (rather than the ViewModel) is what
/// makes <see cref="ContextMenuBuilder"/> a pure function and therefore
/// testable without a UI.
/// </summary>
public sealed record ContextMenuTarget {
    /// <summary>Items under the cursor. Empty for a background click.</summary>
    public IReadOnlyList<FileSystemEntry> Selection { get; init; } = Array.Empty<FileSystemEntry>();

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

    public bool CanUndo { get; init; }


    // --- View state, for the checkmarks in View / Sort by ---------------

    public string ViewMode { get; init; } = "Details";

    public SortKey SortKey { get; init; } = SortKey.Name;

    public bool SortAscending { get; init; } = true;

    public bool GroupFoldersFirst { get; init; } = true;

    public bool IsPreviewVisible { get; init; }


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

    /// <summary>
    /// The listing is the Recycle Bin. Read-only like any shell namespace,
    /// but with one thing you *can* do to its contents — put them back.
    /// </summary>
    public bool IsRecycleBin { get; init; }
}
