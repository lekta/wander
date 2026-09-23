namespace Wander.Core.Layout;

/// <summary>Where a drag can hover to go deeper.</summary>
public enum HoverSurface {
    /// <summary>A line of a folder panel: held there, a closed row with subfolders opens.</summary>
    Panel,

    /// <summary>A row of the file list: held there, the list goes into the folder (decision B18).</summary>
    List,
}


/// <summary>
/// What a drag hovers over.
/// </summary>
/// <param name="Path">The folder under the cursor - for a shortcut, the folder it points at.</param>
/// <param name="Surface">Where it is drawn.</param>
/// <param name="CanOpen">
/// For a panel line: closed, with subfolders to show. For a list row: a
/// folder, or a shortcut to one. False for what is never opened into by a
/// drag: an archive, the Recycle Bin, a shell place.
/// </param>
public sealed record HoverTarget(string Path, HoverSurface Surface, bool CanOpen) {
    /// <summary>The same place under the cursor: the jitter of a hand inside one row changes nothing.</summary>
    public bool SameAs(HoverTarget? other) {
        return other is not null && other.Surface == Surface
            && string.Equals(Trim(other.Path), Trim(Path), StringComparison.OrdinalIgnoreCase);
    }


    private static string Trim(string path) {
        return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }
}


/// <summary>A drag's hover: on what, since when, and whether it has already done its thing there.</summary>
public sealed record DragHoverState(HoverTarget? Target, long SinceMs, bool Done) {
    public static readonly DragHoverState None = new(null, 0, Done: false);
}


/// <summary>What a hover amounts to now.</summary>
public enum DragHoverOutcome {
    /// <summary>Nothing to wait for.</summary>
    Nothing,

    /// <summary>Held not long enough yet: ask again at <see cref="DragHoverDecision.AtMs"/>.</summary>
    Wait,

    /// <summary>Open the panel line <see cref="DragHoverDecision.Path"/>.</summary>
    Expand,

    /// <summary>Go into the folder <see cref="DragHoverDecision.Path"/> in the list.</summary>
    Enter,
}


public readonly record struct DragHoverDecision(DragHoverOutcome Outcome, long AtMs = 0, string? Path = null) {
    public static readonly DragHoverDecision Nothing = new(DragHoverOutcome.Nothing);
}


/// <summary>
/// Drag deeper (U1, REDESIGN 4.13): a drag held over a closed folder in a
/// panel opens it, held over a folder in the list goes into it - after a
/// delay, the way Explorer's tree and Finder do. The same place under the
/// cursor keeps its clock, jitter inside a row included; another place, the
/// drag leaving the window or the drop start it over. A place is acted on
/// once. Never into a container (an archive, the bin), into a folder being
/// dragged or anything under one; nothing is closed or left again when the
/// drop does not happen (decision B19). Facts in - the time comes from the
/// caller - and a decision out; the timer is the caller's.
/// </summary>
public static class DragHover {
    /// <summary>The hover after the cursor was seen over <paramref name="target"/> at <paramref name="nowMs"/>.</summary>
    public static DragHoverState Track(DragHoverState state, HoverTarget? target, long nowMs) {
        if (target is not null && target.SameAs(state.Target)) {
            return state;
        }

        return target is null ? DragHoverState.None : new DragHoverState(target, nowMs, Done: false);
    }

    /// <param name="state">The hover.</param>
    /// <param name="nowMs">The time now.</param>
    /// <param name="delayMs">How long a place has to be held.</param>
    /// <param name="dragged">What is being dragged: never gone into, and nothing under it.</param>
    public static DragHoverDecision Decide(DragHoverState state, long nowMs, int delayMs, IReadOnlyCollection<string> dragged) {
        if (state.Done || state.Target is not { CanOpen: true } target || IsDragged(target.Path, dragged)) {
            return DragHoverDecision.Nothing;
        }

        long due = state.SinceMs + delayMs;
        if (nowMs < due) {
            return new DragHoverDecision(DragHoverOutcome.Wait, due);
        }

        return new DragHoverDecision(
            target.Surface == HoverSurface.Panel ? DragHoverOutcome.Expand : DragHoverOutcome.Enter, Path: target.Path);
    }


    private static bool IsDragged(string path, IReadOnlyCollection<string> dragged) {
        string place = Trim(path);
        foreach (string item in dragged) {
            string root = Trim(item);
            if (string.Equals(place, root, StringComparison.OrdinalIgnoreCase)
                || place.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string Trim(string path) {
        return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }
}
