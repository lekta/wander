namespace Wander.Core.FileSystem;

/// <summary>
/// Strategy a batch copy / move / extract consults about name collisions.
/// Implementations are typically backed by a UI window (interactive
/// resolver) or a fixed policy (tests / scripts).
///
/// <para>
/// Push, not pull: the batch finds every collision before it touches
/// anything and asks about all of them in one call, so the window can show
/// the whole list, let the user decide in any order and change their mind
/// until they press OK - and Cancel there costs nothing, because nothing
/// has moved yet. A collision that appears while the batch is already
/// running (a file that landed in the target folder after the check, or a
/// name inside a merged folder nobody answered for) comes as a second call
/// with that one item.
/// </para>
/// </summary>
public interface IConflictResolver {
    /// <summary>
    /// An answer for every conflict in the request, plus answers for the
    /// pairs found inside folders the user chose to merge; null when the
    /// user backed out of the whole batch. A
    /// <see cref="ConflictResolution.Cancel"/> among the answers means the
    /// same as null - callers treat both as "apply nothing". Never called
    /// with an empty list.
    /// </summary>
    IReadOnlyList<ConflictAnswer>? ResolveAll(ConflictRequest request);
}
