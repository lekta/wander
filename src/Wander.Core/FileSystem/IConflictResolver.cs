namespace Wander.Core.FileSystem;

/// <summary>
/// Strategy that <see cref="FileOperationService"/> consults when a batch
/// copy/move would collide with an existing entry. Implementations are
/// typically backed by a UI dialog (interactive resolver) or a fixed policy
/// (tests / scripts).
/// </summary>
public interface IConflictResolver {
    /// <summary>
    /// Called once per batch with the number of pre-detected conflicts.
    /// Return a non-null value to apply the same decision to all conflicts
    /// without further prompting (Replace all / Skip all / Cancel). Return
    /// null to ask <see cref="Resolve"/> per item.
    /// </summary>
    ConflictResolution? StartBatch(int conflictCount);

    /// <summary>Decision for a single conflict.</summary>
    ConflictResolution Resolve(FileConflictInfo conflict);
}
