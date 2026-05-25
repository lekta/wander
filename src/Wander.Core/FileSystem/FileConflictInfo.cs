namespace Wander.Core.FileSystem;

/// <summary>
/// Snapshot of a name collision: what we're about to write and what's already there.
/// Both <see cref="FileSystemEntry"/> instances are populated so the UI can show
/// size / modified-date side-by-side.
/// </summary>
public sealed record FileConflictInfo(FileSystemEntry Source, FileSystemEntry ExistingTarget);
