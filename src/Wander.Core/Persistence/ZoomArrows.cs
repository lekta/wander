namespace Wander.Core.Persistence;

/// <summary>
/// What the arrow keys do full screen while the picture is zoomed in
/// (decision 2026-10-15): walk the pictures with the zoom kept on the same
/// place, Ctrl+arrows moving it - the way to check a series - or the other
/// way round, as FastStone and XnView have it.
/// </summary>
public enum ZoomArrows {
    /// <summary>Arrows walk, Ctrl+arrows move the zoom a third of the screen.</summary>
    Walk,

    /// <summary>Arrows move the zoom, Ctrl+arrows walk.</summary>
    Move,
}
