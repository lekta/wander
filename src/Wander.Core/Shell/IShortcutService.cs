namespace Wander.Core.Shell;

/// <summary>
/// Create and inspect Windows shortcut files (.lnk). These are regular files
/// that the shell renders with a link overlay; creating them does NOT need
/// admin rights, unlike NTFS symlinks.
/// </summary>
public interface IShortcutService {
    /// <summary>Create a .lnk file at <paramref name="shortcutPath"/> pointing at <paramref name="targetPath"/>.</summary>
    void Create(string targetPath, string shortcutPath);

    /// <summary>Resolve a .lnk to its target path, or null if it isn't a valid shortcut.</summary>
    string? Resolve(string shortcutPath);
}
