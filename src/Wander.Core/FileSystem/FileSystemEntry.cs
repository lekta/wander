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
    bool LinksToDirectory) {

    // Convenience: a .lnk that points at a directory is "directory-like" for
    // sort/open purposes even though its on-disk Kind is still File. Other
    // call sites (sorting in the FS layer, OpenEntry in MainViewModel) read
    // this when they want to treat folder-shortcuts the same as folders.
    public bool IsFolderLike => Kind == EntryKind.Directory || LinksToDirectory;
}
