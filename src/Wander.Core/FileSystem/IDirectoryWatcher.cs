namespace Wander.Core.FileSystem;

/// <summary>
/// One thing that changed in the watched folder.
/// </summary>
/// <param name="Path">
/// Full path of the file or folder involved. Empty when the watcher lost
/// track and cannot say — see <see cref="Structural"/>.
/// </param>
/// <param name="Structural">
/// True when the folder's <em>composition</em> changed: something appeared,
/// disappeared or was renamed. False for a change to a file that was there
/// before and is still there — its size, its timestamp, its contents.
///
/// <para>
/// The distinction is the whole point of this type. "Something changed" can
/// only ever be answered by re-listing the folder, and re-listing rebuilds
/// every row: containers, thumbnails, selection. For the commonest change
/// of all — a rating written into a sidecar beside a photograph — that is a
/// folder that jumps under the cursor in answer to one number. A caller that
/// knows only one file's contents changed can re-read that one row instead.
/// </para>
/// </param>
public readonly record struct DirectoryChange(string Path, bool Structural) {
    /// <summary>The watcher lost track; the folder is in an unknown state and has to be re-listed.</summary>
    public static readonly DirectoryChange Unknown = new("", Structural: true);
}


/// <summary>
/// Watches one folder and says what changed in it, so a listing does not
/// have to be stale until the user presses F5. One folder at a time — the
/// one on screen; navigating hands the watcher a new path.
///
/// <para>
/// Events arrive in bursts (a copy of a hundred files, or one atomic
/// replace, which is three events by itself) and from a background thread,
/// so the caller is expected to coalesce them and to marshal onto its own
/// thread. Wander's own scratch files (<see cref="TransientFiles"/>) are
/// filtered out here rather than by every caller.
/// </para>
/// </summary>
public interface IDirectoryWatcher : IDisposable {
    /// <summary>Something in the watched folder changed. May arrive on any thread.</summary>
    event EventHandler<DirectoryChange>? Changed;

    /// <summary>
    /// Watch <paramref name="path"/> instead of whatever was being watched.
    /// Null or a path that cannot be watched (a shell namespace, a
    /// disconnected share, a drive that just went away) simply means nothing
    /// is watched — the manual refresh is always there.
    /// </summary>
    void Watch(string? path);
}
