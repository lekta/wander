namespace Wander.Core.Icons;

public interface IIconProvider {
    /// <summary>Returns PNG bytes of the system icon for the given path, or null if unavailable.</summary>
    byte[]? GetIcon(string path, IconSize size);
}
