using Wander.Core.Companions;

namespace Wander.Core.FileSystem;

public sealed record FileSystemEntry(
    string Name,
    string FullPath,
    EntryKind Kind,
    long? Size,
    DateTime ModifiedUtc,
    bool IsHidden,
    bool IsReadOnly,
    bool IsSystem,
    bool LinksToDirectory,
    // Original on-disk location for shell-namespace items (currently only
    // Recycle Bin contents — set from the "Original location" shell column).
    // null for ordinary filesystem entries; PreviewController shows it in
    // the footer when present so the user can see where a recycled file
    // came from before deciding to restore it.
    string? OriginalLocation = null,
    // Companion ("integrated") files folded into this row: Sprite.png.meta
    // for Sprite.png, IMG.CR2.pp3 for IMG.CR2. Empty for an ordinary entry
    // and always empty when the integration setting is off — the folder
    // listing is what fills this in, via CompanionResolver.Collapse.
    IReadOnlyList<string>? Companions = null,
    // How the photo is marked up, read out of one of those companions
    // (.pp3 / .xmp) by a pass that runs after the listing has landed — see
    // CompanionMetadataService.WithRatings. null means "not looked at yet"
    // as well as "nothing to say"; the two are the same to everyone above.
    SidecarRating? Rating = null,
    // The line this file matched a content search on, already trimmed for
    // display — see Search/ContentMatcher. null in every ordinary listing,
    // and null for a search hit that matched by name only. Carried on the
    // entry rather than in a side table for the same reason
    // OriginalLocation is: the row on screen is the only place it is ever
    // read, and a parallel dictionary keyed by path would have to be kept
    // in step with a collection that already knows how to carry it.
    string? MatchSnippet = null) {

    /// <summary>True when this row stands for a file plus its sidecar(s).</summary>
    public bool HasCompanions => Companions is { Count: > 0 };

    /// <summary>
    /// Whether two readings of the same file say the same thing.
    ///
    /// <para>
    /// Record equality is not usable for this: <see cref="Companions"/> is a
    /// list, and two enumerations of the same folder produce two instances
    /// of it, so every row with a sidecar would count as changed on every
    /// single refresh — losing its container, and with it the selection.
    /// <see cref="Rating"/> on the other hand is a plain record of two ints
    /// and compares by value, which is what makes a rating arriving count as
    /// a change and reach the screen.
    /// </para>
    /// </summary>
    public bool SaysTheSameAs(FileSystemEntry other) {
        return Name == other.Name
            && Kind == other.Kind
            && Size == other.Size
            && ModifiedUtc == other.ModifiedUtc
            && IsHidden == other.IsHidden
            && IsReadOnly == other.IsReadOnly
            && IsSystem == other.IsSystem
            && LinksToDirectory == other.LinksToDirectory
            && OriginalLocation == other.OriginalLocation
            && Rating == other.Rating
            && SameCompanions(Companions, other.Companions);
    }


    private static bool SameCompanions(IReadOnlyList<string>? a, IReadOnlyList<string>? b) {
        if (a is null || a.Count == 0) {
            return b is null || b.Count == 0;
        }
        if (b is null || a.Count != b.Count) {
            return false;
        }

        for (int i = 0; i < a.Count; i++) {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Folder this entry lives in. Only interesting in a search result
    /// list, where rows come from many folders at once and the name alone
    /// no longer says which file is which.
    /// </summary>
    public string? ParentFolder => Path.GetDirectoryName(FullPath);

    // Convenience: a .lnk that points at a directory is "directory-like" for
    // sort/open purposes even though its on-disk Kind is still File. Other
    // call sites (sorting in the FS layer, OpenEntry in MainViewModel) read
    // this when they want to treat folder-shortcuts the same as folders.
    public bool IsFolderLike => Kind == EntryKind.Directory || LinksToDirectory;
}
