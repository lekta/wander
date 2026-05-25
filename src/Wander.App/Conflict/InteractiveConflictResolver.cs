using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

/// <summary>
/// Interactive resolver that pops modal dialogs on the UI thread. Designed for
/// foreground (synchronous) batch operations — when we move them to async, the
/// caller will need to marshal these calls onto the dispatcher.
/// </summary>
public sealed class InteractiveConflictResolver : IConflictResolver {
    public ConflictResolution? StartBatch(int conflictCount) {
        if (conflictCount <= 1) {
            // For a single conflict, skip the batch question and prompt per-item directly.
            return null;
        }
        return BatchConflictDialog.Show(conflictCount);
    }

    public ConflictResolution Resolve(FileConflictInfo conflict) {
        return ConflictDialog.Show(conflict);
    }
}
