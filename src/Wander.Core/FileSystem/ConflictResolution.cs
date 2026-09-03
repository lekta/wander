namespace Wander.Core.FileSystem;

public enum ConflictResolution {
    Replace,
    Skip,
    Rename,
    Cancel,

    /// <summary>
    /// Both folders stay and their contents combine: what is inside the
    /// source lands inside the existing folder, collision by collision -
    /// each one an answer of its own, nested folders merging in turn. The
    /// folder answer to "keep both"; a file's "keep both" is Rename, and a
    /// Merge on anything but two folders is read as Rename.
    /// </summary>
    Merge,
}
