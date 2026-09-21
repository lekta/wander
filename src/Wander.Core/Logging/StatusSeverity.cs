namespace Wander.Core.Logging;

/// <summary>
/// How much a status line matters (PLAN block 0, step 8). The words stay
/// what they are; the status bar and the journal put a mark in front of a
/// warning and an error, and none in front of news. Set where the line is
/// written - the place that writes it knows what happened.
/// </summary>
public enum StatusSeverity {
    /// <summary>What happened, as expected: copied, moved, undone.</summary>
    Info,

    /// <summary>Done, but not all of it, or not as asked: part failed, a file was held, the bin refused.</summary>
    Warning,

    /// <summary>Did not happen: failed or refused.</summary>
    Error,
}
