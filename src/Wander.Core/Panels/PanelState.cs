using System.Collections.Immutable;

namespace Wander.Core.Panels;

/// <summary>
/// One folder panel as facts (REDESIGN 4.2): the rows it has read, which
/// are open, where the open folder is in it, where its cursor stands.
/// Everything is kept by path - a level read again, a panel rebuilt, a
/// folder renamed keep what was open and where the cursor was without any
/// row object having to survive. What is on screen is derived
/// (<see cref="PanelView"/>).
/// </summary>
public sealed record PanelState {
    /// <summary>The key of the panel's own top rows among <see cref="Levels"/>.</summary>
    public const string TopKey = "";

    public static readonly PanelState Empty = new();


    /// <summary>The rows under each row that has been read, by the row's path (<see cref="PanelPaths.Key"/>); the top rows under <see cref="TopKey"/>.</summary>
    public ImmutableDictionary<string, PanelLevel> Levels { get; init; } =
        ImmutableDictionary.Create<string, PanelLevel>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The rows that are open, by path.</summary>
    public ImmutableHashSet<string> Expanded { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The open folder's row in this panel, or null when it was not opened from here.</summary>
    public string? Location { get; init; }

    /// <summary>The row under the panel's cursor - what the keyboard moves from, and the highlight.</summary>
    public string? Caret { get; init; }

    /// <summary>The row whose name is being edited, or null.</summary>
    public string? Editing { get; init; }

    /// <summary>
    /// A path the panel is being opened down to, level by level, as the
    /// levels on the way are read; null when there is none.
    /// </summary>
    public string? Revealing { get; init; }

    /// <summary>Rows whose children open as soon as their level is read (Alt with the chevron).</summary>
    public ImmutableHashSet<string> OpenChildrenWhenRead { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);


    /// <summary>The panel's top rows: the drives, or the bookmarks.</summary>
    public ImmutableArray<PanelRow> Top => LevelOf(TopKey).Rows;


    /// <summary>The level under <paramref name="path"/> - <see cref="PanelLevel.Unread"/> when it has not been asked for.</summary>
    public PanelLevel LevelOf(string path) {
        return Levels.TryGetValue(PanelPaths.Key(path), out var level) ? level : PanelLevel.Unread;
    }

    /// <summary>The row is open.</summary>
    public bool IsExpanded(string path) {
        return Expanded.Contains(PanelPaths.Key(path));
    }

    /// <summary>The first row on <paramref name="path"/> the panel knows of - on screen or not.</summary>
    public PanelRow? Find(string? path) {
        if (path is null) {
            return null;
        }

        foreach (var row in Top) {
            if (PanelPaths.Same(row.Path, path)) {
                return row;
            }
        }
        foreach (var level in Levels) {
            if (level.Key.Length == 0) {
                continue;
            }
            foreach (var row in level.Value.Rows) {
                if (PanelPaths.Same(row.Path, path)) {
                    return row;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The top row <paramref name="path"/> is reached from: the drive it is
    /// on, the first bookmark it is in or on. Null when no top row holds it -
    /// a folder outside every bookmark, a path on no drive of the panel. A
    /// shell row and a bookmark whose folder is gone hold only themselves.
    /// </summary>
    public PanelRow? TopHolding(string path) {
        foreach (var row in Top) {
            bool leaf = row.Kind == PanelRowKind.Shell || row.IsMissing;
            if (leaf ? PanelPaths.Same(path, row.Path) : PanelPaths.IsUnderOrSelf(path, row.Path)) {
                return row;
            }
        }

        return null;
    }


    /// <summary>This panel with one level replaced.</summary>
    public PanelState WithLevel(string path, PanelLevel level) {
        return this with { Levels = Levels.SetItem(PanelPaths.Key(path), level) };
    }

    /// <summary>This panel with a row opened or closed.</summary>
    public PanelState WithExpanded(string path, bool open) {
        string key = PanelPaths.Key(path);

        return this with { Expanded = open ? Expanded.Add(key) : Expanded.Remove(key) };
    }
}
