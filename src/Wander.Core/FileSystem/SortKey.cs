namespace Wander.Core.FileSystem;

/// <summary>
/// What to sort folder listings by. The set is intentionally small —
/// these mirror the columns the Details view shows; anything more
/// niche (date created, attributes, …) belongs in a future "advanced
/// sort" picker.
/// </summary>
public enum SortKey {
    Name,
    ModifiedDate,
    Size,
    Type,

    /// <summary>
    /// Stars from the photo's sidecar. Unlike the four above, this one is
    /// not something a directory scan knows: the rating arrives with the
    /// pass that reads the sidecars, which is why the listing re-sorts once
    /// that pass lands (see <c>MainViewModel.LoadRatingsAsync</c>).
    /// </summary>
    Rating,
}


/// <summary>
/// Bundle of sort preferences passed to <see cref="IFileSystem.Enumerate"/>.
/// Caller-provided so the FS layer stays free of user-preference state —
/// the same Enumerate call sorts whichever way the caller asks.
/// </summary>
/// <param name="Key">Primary sort column.</param>
/// <param name="Ascending">true = A→Z / oldest→newest / smallest→largest. false = reversed.</param>
/// <param name="GroupFoldersFirst">
/// Explorer parity: when true, folders (and folder-like shortcuts) sort as
/// their own block above plain files. Within each block, the primary key
/// orders normally. When false, everything sorts in one merged stream —
/// useful for "sort by date" when the user just wants newest first
/// regardless of kind.
/// </param>
public sealed record SortOptions(SortKey Key, bool Ascending, bool GroupFoldersFirst) {
    /// <summary>
    /// Sensible defaults: name A→Z, folders on top. Used when a caller
    /// (tree view, tests, anywhere that doesn't expose sort to the user)
    /// doesn't care about the ordering knobs.
    /// </summary>
    public static SortOptions Default { get; } = new(SortKey.Name, true, true);
}
