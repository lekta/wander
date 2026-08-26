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
    /// Where cached thumbnails are kept and how much room they take, for
    /// the settings dialog to show. Null directory = no disk cache.
    /// </summary>
    (string? Directory, long SizeBytes) DescribeCache();
}
