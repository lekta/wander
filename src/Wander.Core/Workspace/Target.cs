using Wander.Core.FileSystem;
using Wander.Core.Panels;

namespace Wander.Core.Workspace;

/// <summary>What kind of thing the next operation is about.</summary>
public enum TargetKind {
    /// <summary>
    /// Nothing: the keyboard is in the list with nothing selected, or in a
    /// panel with no row under its cursor. Delete, Enter and F2 do nothing;
    /// the verbs about a folder - paste, properties, a terminal - are about
    /// the open one.
    /// </summary>
    None,

    /// <summary>Rows of the list's selection.</summary>
    ListRows,

    /// <summary>One row of a folder panel: a folder, by its path.</summary>
    PanelRow,

    /// <summary>The empty space of a folder - only ever the subject of a menu opened there.</summary>
    Background,
}


/// <summary>
/// What the next operation is about (REDESIGN 4.3). Derived, never kept
/// as a fact of its own: <see cref="TargetRules.Of"/> works it out from
/// where the keyboard is, what the list has selected, where each panel's
/// cursor stands and which menu is open. A target nobody stores is a target
/// that cannot go stale - the keyboard leaving a panel used to leave the
/// panel's folder behind as what the next Delete was about.
/// </summary>
public sealed record Target {
    public static readonly Target None = new();


    private Target() {
    }


    public TargetKind Kind { get; private init; }

    /// <summary>The rows of a <see cref="TargetKind.ListRows"/> target, as the list reports them; empty otherwise.</summary>
    public IReadOnlyList<FileSystemEntry> Rows { get; private init; } = Array.Empty<FileSystemEntry>();

    /// <summary>
    /// The list's current row among <see cref="Rows"/> - what one-row verbs
    /// (open, rename, properties) act on. Null unless the target is rows.
    /// </summary>
    public FileSystemEntry? Primary { get; private init; }

    /// <summary>The panel of a <see cref="TargetKind.PanelRow"/>.</summary>
    public Pane Pane { get; private init; }

    /// <summary>The folder of a panel row or of a background; null otherwise.</summary>
    public string? Folder { get; private init; }


    /// <summary>
    /// Rows of the list. No rows is no target. A primary that is not among
    /// the rows - the list reports its current item and its selection as two
    /// separate events - gives way to the first row.
    /// </summary>
    public static Target OfRows(IReadOnlyList<FileSystemEntry> rows, FileSystemEntry? primary) {
        if (rows.Count == 0) {
            return None;
        }

        var main = primary is not null && rows.Any(r => SamePath(r.FullPath, primary.FullPath)) ? primary : rows[0];

        return new Target { Kind = TargetKind.ListRows, Rows = rows, Primary = main };
    }

    /// <summary>The row of a panel on <paramref name="folder"/>; no path is no target.</summary>
    public static Target OfPanelRow(Pane pane, string? folder) {
        return string.IsNullOrEmpty(folder)
            ? None
            : new Target { Kind = TargetKind.PanelRow, Pane = pane, Folder = folder };
    }

    /// <summary>The empty space of <paramref name="folder"/>, right-clicked.</summary>
    public static Target OfBackground(string? folder) {
        return string.IsNullOrEmpty(folder)
            ? None
            : new Target { Kind = TargetKind.Background, Folder = folder };
    }


    /// <summary>
    /// The same target, whatever objects stand for it: rows are replaced on
    /// every re-listing and every rating written, and a target that "changed"
    /// with them would be logged and re-shown for nothing.
    /// </summary>
    public bool SameAs(Target other) {
        if (Kind != other.Kind || Pane != other.Pane || !SamePath(Folder, other.Folder)
            || !SamePath(Primary?.FullPath, other.Primary?.FullPath) || Rows.Count != other.Rows.Count) {
            return false;
        }

        for (int i = 0; i < Rows.Count; i++) {
            if (!SamePath(Rows[i].FullPath, other.Rows[i].FullPath)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>One line for the log: what the target is, in paths.</summary>
    public string Describe() {
        return Kind switch {
            TargetKind.ListRows => Rows.Count == 1
                ? $"list row {Rows[0].FullPath}"
                : $"{Rows.Count} list rows, first {Rows[0].FullPath}",
            TargetKind.PanelRow => $"{Pane} row {Folder}",
            TargetKind.Background => $"background of {Folder}",
            _ => "none",
        };
    }


    private static bool SamePath(string? a, string? b) {
        return a is null || b is null
            ? a is null && b is null
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
