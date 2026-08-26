namespace Wander.Core.FileSystem;

/// <summary>
/// The "what may appear in a listing" rules, as one value. Both the file
/// list and the folder tree filter with it, so a folder hidden in one is
/// hidden in the other; passing it as a record rather than three loose
/// booleans is also what keeps the background enumeration from reading
/// live settings off the UI thread.
/// </summary>
/// <param name="ShowHidden">Show entries carrying the <c>Hidden</c> attribute.</param>
/// <param name="ShowSystem">Show entries carrying the <c>System</c> attribute.</param>
/// <param name="HideSystemRootFolders">
/// Hide the volume-root bookkeeping folders regardless of the two flags
/// above — see <see cref="SystemRootFolders"/>.
/// </param>
public readonly record struct EntryVisibility(
    bool ShowHidden,
    bool ShowSystem,
    bool HideSystemRootFolders) {
    /// <summary>Everything visible — the shape tests and callers without settings use.</summary>
    public static readonly EntryVisibility All = new(true, true, false);


    public bool Allows(FileSystemEntry entry) {
        if (!ShowHidden && entry.IsHidden) {
            return false;
        }
        if (!ShowSystem && entry.IsSystem) {
            return false;
        }

        return !HideSystemRootFolders || !SystemRootFolders.IsSystemRoot(entry.FullPath);
    }
}
