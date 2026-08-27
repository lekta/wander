namespace Wander.Core.FileSystem;

/// <summary>
/// Watches one folder and says when something in it changed, so a listing
/// does not have to be stale until the user presses F5. One folder at a
/// time — the one on screen; navigating hands the watcher a new path.
///
/// <para>
/// Deliberately says <b>that</b> something changed and not <b>what</b>: the
/// answer is always "re-list the folder", and a per-event model would invite
/// callers to patch rows one by one, which is where every subtle
/// list-out-of-sync bug comes from. Events can arrive in bursts (a copy of
/// a hundred files) and from a background thread, so the caller is expected
/// to coalesce them and to marshal onto its own thread.
/// </para>
/// </summary>
public interface IDirectoryWatcher : IDisposable {
    /// <summary>
    /// Watch <paramref name="path"/> instead of whatever was being watched.
    /// Null or a path that cannot be watched (a shell namespace, a
    /// disconnected share, a drive that just went away) simply means nothing
    /// is watched — the manual refresh is always there.
    /// </summary>
    void Watch(string? path);

    /// <summary>Something in the watched folder changed. May arrive on any thread.</summary>
    event EventHandler? Changed;
}
