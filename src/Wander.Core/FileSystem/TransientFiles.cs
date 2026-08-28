namespace Wander.Core.FileSystem;

/// <summary>
/// Names Wander gives its own scratch files. Shared knowledge rather than a
/// detail of whoever writes them: the folder watcher sees these appear and
/// disappear, and a watcher that mistook one for a real file would answer a
/// rating written into a sidecar with a full re-listing of the folder.
/// </summary>
public static class TransientFiles {
    /// <summary>
    /// Suffix of the file <see cref="IFileSystem.ReplaceAtomic"/> writes
    /// beside its target before swapping it in. It exists for a few
    /// milliseconds and is nobody's business but ours.
    /// </summary>
    public const string ReplaceSuffix = ".wander-tmp";

    // Windows' own scratch file. ReplaceFile — which File.Replace calls, and
    // which is what makes the swap atomic — writes a backup of the target
    // beside it as "<target>~RF<hex>.TMP" and deletes it again a moment
    // later. It is not ours and it is not documented anywhere near the API
    // that produces it, which is exactly why it belongs in this list: it
    // cost an afternoon to find, appearing and vanishing in the watcher's
    // ear and making every rating written into a sidecar look like two
    // files coming and going in the folder.
    private const string WindowsReplaceMarker = "~RF";
    private const string WindowsReplaceSuffix = ".TMP";


    /// <summary>
    /// True for a scratch file that exists only for the length of a write —
    /// ours or Windows'. Nothing outside the filesystem layer should ever
    /// see one, and a folder listing that reacted to them would rebuild
    /// itself on every saved rating.
    /// </summary>
    public static bool IsTransient(string path) {
        if (path.EndsWith(ReplaceSuffix, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        string name = Path.GetFileName(path);
        if (!name.EndsWith(WindowsReplaceSuffix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        int marker = name.LastIndexOf(WindowsReplaceMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0) {
            return false;
        }

        // Everything between the marker and the extension is hex, or this is
        // somebody's file that merely looks the part. A false positive here
        // costs one missed refresh of one file, so the check is worth being
        // exact about rather than clever.
        for (int i = marker + WindowsReplaceMarker.Length; i < name.Length - WindowsReplaceSuffix.Length; i++) {
            if (!Uri.IsHexDigit(name[i])) {
                return false;
            }
        }

        return true;
    }
}
