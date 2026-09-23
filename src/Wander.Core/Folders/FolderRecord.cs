namespace Wander.Core.Folders;

/// <summary>
/// What Wander remembers about one folder, in its own <c>folders.json</c>
/// (decision Z1: nothing is written into the folder itself, so this works
/// on read-only media and inside archives). Today that is the view the
/// user pinned; the record is built to take more (a sort order, a gallery
/// background) as further optional fields - JSON tolerates a field an
/// older file does not have, so adding one does not change the file's
/// version.
/// </summary>
/// <param name="Path">The folder, as the user saw it; the key. Compared without case and without a trailing separator.</param>
/// <param name="CreatedUtc">
/// When the folder was created, from the file system - the second half of
/// the key. A folder renamed outside Wander keeps its creation time, and
/// that is how its record finds it again (<see cref="FolderSettingsBook.Adopt"/>).
/// Null where it could not be read, or for a folder inside an archive or
/// the Recycle Bin, whose records go by path alone.
/// </param>
/// <param name="LastVisit">The day the folder was last opened; the ageing order when the book is full.</param>
/// <param name="View">The view pinned to this folder, or null for "chosen automatically".</param>
public sealed record FolderRecord(string Path, DateTime? CreatedUtc, DateOnly LastVisit, ViewMode? View) {
    /// <summary>True when the record says nothing any more and can be dropped.</summary>
    public bool IsEmpty => View is null;
}
