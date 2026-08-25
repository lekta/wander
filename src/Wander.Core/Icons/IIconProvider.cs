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
}
