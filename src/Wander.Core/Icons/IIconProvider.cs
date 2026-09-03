namespace Wander.Core.Icons;

public interface IIconProvider {
    /// <summary>Returns PNG bytes of the system icon for the given path, or null if unavailable.</summary>
    byte[]? GetIcon(string path, IconSize size);

    /// <summary>
    /// Returns the icon only if it is already in memory; never touches the
    /// shell or the disk. Lets a caller that loads icons in the background
    /// stay synchronous for the already-known ones — without it, a
    /// re-scrolled list would round-trip through a worker thread and blink
    /// its icons on every pass.
    /// </summary>
    byte[]? TryGetCachedIcon(string path, IconSize size);

    /// <summary>
    /// Applies the user's cache limits. Called at startup and whenever the
    /// settings change, so the provider never has to read settings itself.
    /// </summary>
    void ConfigureCache(ThumbnailCacheOptions options);

    /// <summary>Throws away every cached thumbnail, in memory and on disk.</summary>
    void ClearCache();

    /// <summary>
    /// Forgets everything cached about one path, in memory and on disk.
    ///
    /// <para>
    /// The disk tier keys on the file's stamp, so an edited file normally
    /// lands on a key of its own and the old entry is simply orphaned. The
    /// memory tier cannot: it is keyed by path alone, which is what makes
    /// it fast enough to answer on the UI thread. So when the watcher says
    /// a file changed - or vanished and came back, which is what replacing
    /// a picture with another under the same name looks like - the entry
    /// has to be dropped by hand, or the folder goes on showing the picture
    /// that is no longer there.
    /// </para>
    /// </summary>
    void Forget(string path);

    /// <summary>
    /// Forgets one path, but only if what is cached for it was made from a
    /// different reading of the file. Returns true when something was
    /// dropped, so the caller can redraw.
    ///
    /// <para>
    /// This is the answer for the changes nobody watched: the folder was
    /// listed, left, edited by another program, and walked back into. No
    /// watcher event ever arrived, and the memory cache would go on serving
    /// the old picture for the rest of the session. The stamp comes from
    /// the listing that is being published, so the check costs a dictionary
    /// probe per row and no disk access at all.
    /// </para>
    /// </summary>
    bool ForgetIfChanged(string path, FileStamp stamp);

    /// <summary>
    /// Where cached thumbnails are kept and how much room they take, for
    /// the settings dialog to show. Null directory = no disk cache.
    /// </summary>
    (string? Directory, long SizeBytes) DescribeCache();
}
