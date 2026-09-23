namespace Wander.Core.Panels;

/// <summary>What a row of a folder panel stands on.</summary>
public enum PanelRowKind {
    /// <summary>A folder on disk.</summary>
    Folder,

    /// <summary>A drive's root, at the top of the drives panel.</summary>
    Drive,

    /// <summary>An archive the shell opens as a folder, among the folders.</summary>
    Archive,

    /// <summary>A folder inside an archive.</summary>
    ArchiveFolder,

    /// <summary>A shell location - the Recycle Bin. A leaf: nothing under it is shown.</summary>
    Shell,
}


/// <summary>Why a row is in the bookmarks panel, if it is.</summary>
public enum PanelRowRole {
    /// <summary>An ordinary row: a folder, a drive, or anything under a bookmark.</summary>
    Normal,

    /// <summary>A special folder the settings switch on (Downloads, Documents, the bin...).</summary>
    BuiltInBookmark,

    /// <summary>One of the user's own bookmarks.</summary>
    OwnBookmark,
}


/// <summary>What is known of a row's subfolders - what decides its chevron.</summary>
public enum ChildrenKnown {
    /// <summary>Not asked yet: the chevron is drawn on faith, the way Explorer draws it.</summary>
    Unknown,

    /// <summary>It has some.</summary>
    Yes,

    /// <summary>It has none: no chevron.</summary>
    No,
}


/// <summary>
/// One row of a folder panel - a value: nothing in it reads the disk, and a
/// change is a new row. Whether it is open, under the cursor or the open
/// folder's place is the panel's state (<see cref="PanelState"/>), kept by
/// path, so a row read again keeps all of that.
/// </summary>
/// <param name="Path">The folder; a shell path for a shell row.</param>
/// <param name="Name">The label: the folder's name, or a bookmark's own title.</param>
/// <param name="Kind">What it stands on.</param>
public sealed record PanelRow(string Path, string Name, PanelRowKind Kind) {
    /// <summary>A hidden folder - drawn faded.</summary>
    public bool IsHidden { get; init; }

    /// <summary>What is known of its subfolders.</summary>
    public ChildrenKnown Children { get; init; } = ChildrenKnown.Unknown;

    /// <summary>A bookmark whose folder is gone: greyed, no chevron, still there until the user removes it.</summary>
    public bool IsMissing { get; init; }

    /// <summary>Why it is in the bookmarks panel.</summary>
    public PanelRowRole Role { get; init; }

    /// <summary>The first of the user's own bookmarks under the special folders: a rule is drawn above it.</summary>
    public bool StartsSection { get; init; }


    /// <summary>A chevron is drawn: subfolders there, or not asked about yet.</summary>
    public bool HasChevron => Children != ChildrenKnown.No && !IsMissing && Kind != PanelRowKind.Shell;

    /// <summary>
    /// Its subfolders can be asked of the disk off the UI thread to settle
    /// the chevron. Not an archive: answering would open every archive in a
    /// folder through the shell on every expansion.
    /// </summary>
    public bool IsProbed => Kind is PanelRowKind.Folder or PanelRowKind.Drive && !IsMissing;

    /// <summary>
    /// F2 can rename it: a folder on disk, not a drive, not a bookmark whose
    /// folder is gone, and not a built-in bookmark - that label is the name
    /// Windows gives the folder, not the folder's own.
    /// </summary>
    public bool IsRenamable => Kind == PanelRowKind.Folder && !IsMissing && Role != PanelRowRole.BuiltInBookmark
        && PanelPaths.Parent(Path) is not null;
}
