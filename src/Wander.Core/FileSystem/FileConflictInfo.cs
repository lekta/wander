namespace Wander.Core.FileSystem;

/// <summary>
/// Snapshot of a name collision: what we're about to write and what's already
/// there. Both <see cref="FileSystemEntry"/> instances are populated so the
/// UI can show size / modified-date side-by-side; what the two have in
/// common is worked out by <see cref="ConflictVerdict"/>. One file is one
/// collision: a sidecar collides, is asked about and answers on its own,
/// because its content changes independently of its main file's.
/// </summary>
/// <param name="IsMove">
/// The source leaves its folder on Replace and Rename, and stays put on
/// Skip - for a copy Skip changes nothing, for a move it is the one answer
/// that leaves the file where it was.
/// </param>
/// <param name="SourceReachable">
/// The source can be opened through <see cref="IFileSystem"/>: its bytes
/// compared and, for a folder, its contents walked for a merge. False for
/// a folder inside an archive, which only the shell reads - such a pair is
/// decided on name, size and date alone, and cannot be merged, only
/// replaced, kept, or extracted under a new name.
/// </param>
/// <param name="ReadablePath">
/// Where the source's bytes can be read, when that is not
/// <see cref="Source"/>'s own path. Set for a file inside an archive: only
/// the shell can read one, so a scratch copy is unpacked before the
/// question is asked and the comparison reads that instead. Null means the
/// source reads from where it says it is - every ordinary copy or move.
/// The answers are still keyed by <see cref="Source"/>'s path: the copy is
/// where the bytes are, not what the user is deciding about.
/// </param>
public sealed record FileConflictInfo(
    FileSystemEntry Source,
    FileSystemEntry ExistingTarget,
    bool IsMove = false,
    bool SourceReachable = true,
    string? ReadablePath = null) {

    /// <summary>Where to open the source's bytes.</summary>
    public string SourceReadPath => ReadablePath ?? Source.FullPath;
}
