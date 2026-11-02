namespace Wander.Core.FileSystem;

/// <summary>
/// The "what may appear in a listing" rules, as one value. Both the file
/// list and the folder tree filter with it, so a folder hidden in one is
/// hidden in the other; passing it as a record rather than three loose
/// booleans is also what keeps the background enumeration from reading
/// live settings off the UI thread.
/// </summary>
/// <param name="ShowHidden">Show entries carrying the <c>Hidden</c> attribute.</param>
/// <param name="ShowSystem">
/// Show protected operating system files — the ones carrying <c>Hidden</c>
/// <em>and</em> <c>System</c> together. See <see cref="Allows"/> for why the
/// pair rather than <c>System</c> alone.
/// </param>
/// <param name="HideSystemRootFolders">
/// Hide Windows' bookkeeping regardless of the two flags above: the
/// volume-root folders (<see cref="SystemRootFolders"/>) and the files it
/// leaves in any folder (<see cref="ServiceFiles"/>). Without
/// <paramref name="ShowHidden"/> they stay hidden whatever this says.
/// </param>
public readonly record struct EntryVisibility(
    bool ShowHidden,
    bool ShowSystem,
    bool HideSystemRootFolders) {
    /// <summary>Everything visible — the shape tests and callers without settings use.</summary>
    public static readonly EntryVisibility All = new(true, true, false);


    /// <summary>
    /// Whether the entry may appear in a listing.
    ///
    /// <para>
    /// "System" means <c>Hidden</c> and <c>System</c> <em>together</em> —
    /// what Windows calls a protected operating system file, and what
    /// Explorer's own second checkbox controls. The <c>System</c> attribute
    /// on its own is not a signal about anything: Windows sets it on every
    /// folder that carries a <c>desktop.ini</c>, which is every folder whose
    /// icon was ever customised. Treating those as system hid ordinary
    /// user folders that Explorer shows without comment.
    /// </para>
    /// </summary>
    public bool Allows(FileSystemEntry entry) {
        if (!ShowSystem && entry.IsHidden && entry.IsSystem) {
            return false;
        }
        if (!ShowHidden && entry.IsHidden) {
            return false;
        }
        if (ShowHidden && !HideSystemRootFolders) {
            return true;
        }

        // The bookkeeping counts as hidden whatever its attributes say: a
        // desktop.ini an installer wrote carries none.
        return !ServiceFiles.Is(entry) && !SystemRootFolders.IsSystemRoot(entry.FullPath);
    }
}
