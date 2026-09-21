namespace Wander.Core.FileSystem;

/// <summary>
/// Identifier returned by <see cref="IRecycleBin.Send"/>. Carries the
/// information needed by <see cref="IRecycleBin.Restore"/> to find the same
/// item back in the bin. Records survive across process restart since
/// they're plain data, but the undo stack currently does not - so today this
/// only matters within a session.
///
/// <para>
/// Three ways to find the item, best first. <see cref="BinItemId"/> is what
/// <see cref="IRecycleBin.Send"/> got back from the bin itself - opaque to
/// Core, direct for the platform. <see cref="BinFilePath"/> is the file the
/// bin keeps the item in: what a listing of the bin has for every row. The
/// original path plus the moment of deletion is the fallback, and the only
/// one of the three that can pick the wrong item - the same path deleted
/// twice within one tick of the bin's clock.
/// </para>
/// </summary>
public sealed record RecycleHandle(
    string OriginalPath, DateTime DeletedAtUtc, string? BinItemId = null, string? BinFilePath = null);


/// <summary>
/// The item cannot go to the recycle bin at all - its path (or one inside
/// it) is longer than the bin takes, or the drive has no bin. Thrown
/// <b>instead of</b> deleting: left to itself the shell deletes such an item
/// for good without a word (stand of 2026-09-21), and a delete nobody can
/// undo is only ever done on an explicit answer.
/// </summary>
public sealed class RecycleUnavailableException : IOException {
    public RecycleUnavailableException(string path, RecycleUnavailableReason reason)
        : base($"'{path}' cannot be moved to the recycle bin: {Explain(reason)}") {
        ItemPath = path;
        Reason = reason;
    }


    public string ItemPath { get; }

    /// <summary>Why - what the question to the user names, in its own words.</summary>
    public RecycleUnavailableReason Reason { get; }


    private static string Explain(RecycleUnavailableReason reason) {
        return reason switch {
            RecycleUnavailableReason.PathTooLong => "the path, or one inside it, is too long for the bin",
            _ => "there is no recycle bin for it",
        };
    }
}


/// <summary>Why the recycle bin will not take an item.</summary>
public enum RecycleUnavailableReason {
    /// <summary>The item's path, or a path inside the folder, is longer than the bin takes.</summary>
    PathTooLong,

    /// <summary>The drive has no bin: the shell was going to destroy the item rather than recycle it.</summary>
    NoBin,
}


/// <summary>
/// One item of <see cref="IRecycleBin.SendMany"/>: a handle when it went to
/// the bin, an error when it did not, neither when the batch was cancelled
/// before its turn.
/// </summary>
public sealed record RecycleResult(string Path, RecycleHandle? Handle, Exception? Error);


/// <summary>
/// Wrapper around the Windows recycle bin. Send moves a file or folder to
/// the bin (so it shows up in <c>shell:RecycleBinFolder</c>); Restore puts
/// it back at <see cref="RecycleHandle.OriginalPath"/>.
/// </summary>
public interface IRecycleBin {
    /// <summary>
    /// Move the item at <paramref name="path"/> to the recycle bin. Throws on
    /// failure (path missing, no permission, in use), and
    /// <see cref="RecycleUnavailableException"/> when the bin cannot take it -
    /// the item is then still where it was.
    /// </summary>
    RecycleHandle Send(string path);

    /// <summary>
    /// <see cref="Send"/> for a batch, one result per path, in the order
    /// given; it does not throw for an item. The platform does the batch in
    /// one go - a third of the time (stand of 2026-09-21) - which is the
    /// reason this exists; the default is the plain loop.
    /// </summary>
    /// <param name="paths">What to recycle.</param>
    /// <param name="onItemDone">Called with each path as its answer becomes final, from whatever thread does the work.</param>
    /// <param name="ct">Stops the batch between items; the rest come back with neither handle nor error.</param>
    IReadOnlyList<RecycleResult> SendMany(
        IReadOnlyList<string> paths, Action<string>? onItemDone = null, CancellationToken ct = default) {
        var results = new List<RecycleResult>(paths.Count);
        foreach (string path in paths) {
            if (ct.IsCancellationRequested) {
                results.Add(new RecycleResult(path, null, null));
                continue;
            }

            try {
                results.Add(new RecycleResult(path, Send(path), null));
            } catch (Exception ex) {
                results.Add(new RecycleResult(path, null, ex));
            }
            onItemDone?.Invoke(path);
        }

        return results;
    }

    /// <summary>
    /// Restore a previously sent item to its original location. Throws if the
    /// item is no longer in the bin (user emptied it). A target path occupied
    /// in the meantime is never overwritten: the item comes back under the
    /// next free numbered name (<see cref="UniqueNames"/>).
    /// </summary>
    void Restore(RecycleHandle handle);
}
