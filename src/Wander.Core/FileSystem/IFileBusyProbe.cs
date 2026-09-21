namespace Wander.Core.FileSystem;

/// <summary>
/// "Would a delete, a move or a rename of this path fail because somebody
/// has it open" - asked before the operation, in a couple of milliseconds,
/// instead of found out from it in a second (the shell engine takes 1021 ms
/// to report a sharing violation, stand of 2026-09-21). Who the holder is
/// is a different, slower question: <c>IFileLockInspector</c>.
/// </summary>
public interface IFileBusyProbe {
    /// <summary>False for a path that is free, missing, or a folder - only a file can be asked cheaply.</summary>
    bool IsBusy(string path);
}
