namespace Wander.Core.Persistence;

/// <summary>How the last read of a data file went (<c>state.json</c>, <c>pins.json</c> and the rest).</summary>
public enum StoreReadOutcome {
    /// <summary>No file: a first start, or a file nobody has written yet.</summary>
    Missing,
    /// <summary>Read and understood.</summary>
    Read,
    /// <summary>There, and not understood: broken JSON, a field of a type this build does not know.</summary>
    Broken,
    /// <summary>There, and could not be opened: held by an antivirus or a backup, denied.</summary>
    Unavailable,
    /// <summary>Written by a newer build than this one (its <c>Version</c>, read apart from the body).</summary>
    Newer,
}

/// <summary>What a store does after a read.</summary>
/// <param name="UseDefaults">Start from the defaults: nothing of the file is in memory.</param>
/// <param name="MayWrite">The store may write the file - until the next read says otherwise.</param>
/// <param name="MoveAsideFirst">Before the first write, the file goes to <c>&lt;name&gt;.bad</c>.</param>
/// <param name="ClosedForSession">No write at all until the session ends, whatever later reads say.</param>
public readonly record struct StoreReadPlan(bool UseDefaults, bool MayWrite, bool MoveAsideFirst, bool ClosedForSession = false);


/// <summary>
/// The one rule every data file's store follows (audit 2026-11-18, P6, V14,
/// L8): a file that did not read is never written over as it is. Every
/// writer does read-modify-write, so a write after a failed read puts the
/// defaults in place of what the file held - templates, window places,
/// bookmarks. A broken file goes aside to <c>.bad</c> first, where it can be
/// looked at; one that could not be opened is left alone; one of a newer
/// shape is the newer build's to write. The stores (Platform,
/// <c>Persistence</c>) carry it out.
/// </summary>
public static class StoreRead {
    /// <param name="firstRead">
    /// The first read of the file this session. The app builds its model
    /// from that one: when it could not be opened the model holds the
    /// defaults, and a write from it later - once the file opens again -
    /// would put them in place of the bookmarks and the settings. So that
    /// closes writing for the session; a later read that fails closes it
    /// only until a read gets through.
    /// </param>
    public static StoreReadPlan Plan(StoreReadOutcome outcome, bool firstRead) {
        return outcome switch {
            StoreReadOutcome.Missing => new StoreReadPlan(UseDefaults: true, MayWrite: true, MoveAsideFirst: false),
            StoreReadOutcome.Read => new StoreReadPlan(UseDefaults: false, MayWrite: true, MoveAsideFirst: false),
            StoreReadOutcome.Broken => new StoreReadPlan(UseDefaults: true, MayWrite: true, MoveAsideFirst: true),
            StoreReadOutcome.Unavailable => new StoreReadPlan(UseDefaults: true, MayWrite: false, MoveAsideFirst: false, ClosedForSession: firstRead),
            // What of it read is used as it is - only a body that did not
            // read leaves the defaults; either way the newer build writes it.
            StoreReadOutcome.Newer => new StoreReadPlan(UseDefaults: false, MayWrite: false, MoveAsideFirst: false),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };
    }
}
