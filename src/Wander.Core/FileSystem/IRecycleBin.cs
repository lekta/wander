namespace Wander.Core.FileSystem;

/// <summary>
/// Identifier returned by <see cref="IRecycleBin.Send"/>. Carries the
/// information needed by <see cref="IRecycleBin.Restore"/> to find the same
/// item back in the bin (its original full path and the moment we deleted
/// it). Records survive across process restart since they're plain data,
/// but the undo stack currently does not — so today this only matters
/// within a session.
/// </summary>
public sealed record RecycleHandle(string OriginalPath, DateTime DeletedAtUtc);


/// <summary>
/// Wrapper around the Windows recycle bin. Send moves a file or folder to
/// the bin (so it shows up in <c>shell:RecycleBinFolder</c>); Restore puts
/// it back at <see cref="RecycleHandle.OriginalPath"/>.
/// </summary>
public interface IRecycleBin {
    /// <summary>
    /// Move the item at <paramref name="path"/> to the recycle bin. Throws on
    /// failure (path missing, no permission, recycle disabled for that drive).
    /// </summary>
    RecycleHandle Send(string path);

    /// <summary>
    /// Restore a previously sent item to its original location. Throws if the
    /// item is no longer in the bin (user emptied it) or cannot be restored
    /// (target path now occupied).
    /// </summary>
    void Restore(RecycleHandle handle);
}
