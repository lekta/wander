using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Workspace;

/// <summary>What the preview panes follow: rows of the list, a folder of a panel, or the open folder.</summary>
public enum PreviewSubjectKind {
    /// <summary>Nothing targeted: the pane describes the folder open in the list.</summary>
    OpenFolder,

    /// <summary>Rows of the list.</summary>
    Rows,

    /// <summary>
    /// A panel row's folder. Its row is not in the listing, so the caller
    /// reads it (off the UI thread) and shows it once it has it.
    /// </summary>
    Folder,
}


/// <summary>
/// What the two preview panes show for a target (REDESIGN 4.5): the footer
/// describes <see cref="Selection"/>, the picture is <see cref="Primary"/>,
/// and exactly two files the pane can draw split it in two
/// (<see cref="PreviewPair"/>).
/// </summary>
public sealed record PreviewSubject {
    public static readonly PreviewSubject OpenFolder = new();


    private PreviewSubject() {
    }


    public PreviewSubjectKind Kind { get; private init; }

    /// <summary>What the footer describes: the target's rows; empty for a folder or the open folder.</summary>
    public IReadOnlyList<FileSystemEntry> Selection { get; private init; } = Array.Empty<FileSystemEntry>();

    /// <summary>The file the main pane shows: the upper one of a pair, otherwise the active row.</summary>
    public FileSystemEntry? Primary { get; private init; }

    /// <summary>The two files side by side, upper one first; null when the target is not such a pair.</summary>
    public (FileSystemEntry First, FileSystemEntry Second)? Pair { get; private init; }

    /// <summary>The panel row's folder, for <see cref="PreviewSubjectKind.Folder"/>.</summary>
    public string? Folder { get; private init; }


    /// <summary>
    /// The subject of <paramref name="target"/>. In a selection of several
    /// rows the active one is the row with the focus rectangle
    /// (<paramref name="caretPath"/>) when it is among them - the one a
    /// Ctrl+click just added, which is what "show me this one too" means;
    /// otherwise the list's current row.
    /// </summary>
    /// <param name="listing">The rows on screen: which of a pair is the upper one.</param>
    public static PreviewSubject Of(Target target, string? caretPath, IReadOnlyList<FileSystemEntry> listing) {
        switch (target.Kind) {
            case TargetKind.ListRows:
                var pair = PreviewPair.Of(target.Rows, listing);
                var active = target.Primary;
                if (target.Rows.Count > 1 && caretPath is { Length: > 0 }) {
                    active = target.Rows.FirstOrDefault(r => string.Equals(r.FullPath, caretPath, StringComparison.OrdinalIgnoreCase))
                        ?? active;
                }

                return new PreviewSubject {
                    Kind = PreviewSubjectKind.Rows,
                    Selection = target.Rows,
                    Primary = pair is { } p ? p.First : active,
                    Pair = pair,
                };

            case TargetKind.PanelRow:
                return new PreviewSubject { Kind = PreviewSubjectKind.Folder, Folder = target.Folder };

            default:
                return OpenFolder;
        }
    }
}
