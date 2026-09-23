using System.Collections.Immutable;

namespace Wander.Core.Panels;

/// <summary>Where the rows of a level are.</summary>
public enum LevelState {
    /// <summary>Never read, or dropped: the rows are not known.</summary>
    Unread,

    /// <summary>
    /// A read is out, under <see cref="PanelLevel.Epoch"/>. The rows are the
    /// last ones known - none on a first read, the old ones on a re-read, so
    /// a branch does not blink while it is asked again.
    /// </summary>
    Reading,

    /// <summary>The rows are the ones the disk last answered with.</summary>
    Loaded,
}


/// <summary>
/// The rows under one row of a panel - or the panel's top rows. A read of
/// the level carries the epoch it was asked under; an answer with another
/// epoch is about a question nobody is asking any more (the branch closed,
/// or was asked again) and is dropped.
/// </summary>
public sealed record PanelLevel(LevelState State, ImmutableArray<PanelRow> Rows, int Epoch) {
    public static readonly PanelLevel Unread = new(LevelState.Unread, ImmutableArray<PanelRow>.Empty, 0);
}
