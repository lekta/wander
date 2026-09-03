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
/// an entry inside an archive, which only the shell reads - such a pair is
/// decided on name, size and date alone, and a folder among them cannot be
/// merged, only replaced, kept, or extracted under a new name.
/// </param>
public sealed record FileConflictInfo(
    FileSystemEntry Source,
    FileSystemEntry ExistingTarget,
    bool IsMove = false,
    bool SourceReachable = true);
