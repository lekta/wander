using Wander.Core.Menu;

namespace Wander.Core.Workspace;

/// <summary>
/// What is true of the place a target is in - the folder its rows are
/// listed in, or a panel row's own folder. Which verbs may run is a question
/// about that place and not about the folder open in the list: a folder
/// right-clicked in the drives tree while the Recycle Bin is open is still
/// an ordinary folder, with every verb an ordinary folder has.
/// </summary>
/// <param name="IsReadOnly">Nothing may be written there: the Recycle Bin, an archive.</param>
/// <param name="IsRecycleBin">The Recycle Bin, where rows can be put back.</param>
/// <param name="IsArchive">Inside an archive.</param>
public sealed record PlaceFacts(bool IsReadOnly, bool IsRecycleBin, bool IsArchive) {
    /// <summary>An ordinary folder on disk.</summary>
    public static readonly PlaceFacts Ordinary = new(false, false, false);
}


/// <summary>
/// A menu's own snapshot, taken the moment it opens (REDESIGN 4.3): its
/// subject - rows of the list, a row of a panel, or the empty space of a
/// folder - and the facts about the place of that subject. The menu is
/// built from it (<see cref="ToMenuTarget"/>), and every item runs with it
/// as its parameter: what the item acts on was fixed when the menu was
/// shown, not looked up again when WPF gets round to running the item -
/// after the menu has closed and the keyboard has gone back wherever it was.
/// </summary>
public sealed record MenuContext {
    /// <summary>What the menu is about.</summary>
    public required Target Subject { get; init; }

    /// <summary>The place of <see cref="Subject"/>.</summary>
    public PlaceFacts Place { get; init; } = PlaceFacts.Ordinary;

    /// <summary>The clipboard holds something, and the place takes it.</summary>
    public bool CanPaste { get; init; }

    /// <summary>
    /// Every row of the subject is an archive Wander can open - what earns
    /// "Extract". Computed by the caller: which extensions count is a
    /// property of the machine.
    /// </summary>
    public bool SelectionIsArchive { get; init; }


    /// <summary>
    /// The snapshot for <paramref name="subject"/>. A paste is offered only
    /// where the place can be written to; a panel row is never an archive of
    /// rows to extract.
    /// </summary>
    public static MenuContext For(Target subject, PlaceFacts place, bool clipboardHasContent, bool selectionIsArchive) {
        return new MenuContext {
            Subject = subject,
            Place = place,
            CanPaste = clipboardHasContent && !place.IsReadOnly,
            SelectionIsArchive = subject.Kind == TargetKind.ListRows && selectionIsArchive,
        };
    }


    /// <summary>
    /// What <see cref="ContextMenuBuilder"/> is told. A panel row stands in
    /// as one folder row of its own, and the folder the shell's verbs run in
    /// is that folder; the list's rows and its empty space are in the open
    /// folder. The catalog, the missing tools and the debug switch are the
    /// caller's to add - they are settings, not part of the snapshot.
    /// </summary>
    public ContextMenuTarget ToMenuTarget(string? openFolder, MenuPlace place = MenuPlace.Context) {
        return new ContextMenuTarget {
            Place = place,
            Selection = TargetRules.Items(Subject),
            FolderPath = Subject.Kind is TargetKind.PanelRow or TargetKind.Background ? Subject.Folder : openFolder,
            IsBackground = Subject.Kind == TargetKind.Background,
            IsPanelRow = Subject.Kind == TargetKind.PanelRow,
            IsReadOnlyLocation = Place.IsReadOnly,
            IsRecycleBin = Place.IsRecycleBin,
            IsArchive = Place.IsArchive,
            SelectionIsArchive = SelectionIsArchive,
            CanPaste = CanPaste,
        };
    }
}
