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
    IReadOnlyList<string>? Companions = null) {

    /// <summary>True when this row stands for a file plus its sidecar(s).</summary>
    public bool HasCompanions => Companions is { Count: > 0 };

    // Convenience: a .lnk that points at a directory is "directory-like" for
    // sort/open purposes even though its on-disk Kind is still File. Other
    // call sites (sorting in the FS layer, OpenEntry in MainViewModel) read
    // this when they want to treat folder-shortcuts the same as folders.
    public bool IsFolderLike => Kind == EntryKind.Directory || LinksToDirectory;
}
