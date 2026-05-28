using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Undo;

namespace Wander.Core.FileSystem;

/// <summary>
/// Single entry point for every file-modifying operation in Wander.
/// Registered as a singleton in the service locator so the same instance —
/// and the same undo stack — sees every call site (VM, drop handlers,
/// future scripting, etc.).
///
/// <para>
/// This class is a thin facade: single-item ops (copy / move / delete /
/// rename / create folder) are implemented inline, but the batch and async
/// variants are delegated to <see cref="BatchExecutor"/>. Result types
/// (<see cref="BatchItemResult"/>, <see cref="DeleteResult"/>) live at the
/// namespace level so callers don't need to reach through the facade.
/// </para>
///
/// <para>
/// Each successful op also lands as an <see cref="IUndoableAction"/> on the
/// shared <see cref="UndoService"/>. Pure read paths still live on
/// <see cref="IFileSystem"/> and bypass this service.
/// </para>
/// </summary>
public sealed class FileOperationService {
    private readonly IFileSystem _fs;
    private readonly IRecycleBin _bin;
    private readonly UndoService _undo;
    private readonly ILogger _log;
    private readonly BatchExecutor _batch;


    /// <summary>Full ctor — used by tests and the production registration in PlatformBootstrapper.</summary>
    public FileOperationService(IFileSystem fs, IRecycleBin bin, UndoService undo, OperationTracker tracker, ILogger log) {
        _fs = fs;
        _bin = bin;
        _undo = undo;
        _log = log;
        _batch = new BatchExecutor(fs, bin, undo, tracker, log);
    }

    /// <summary>Convenience ctor that pulls collaborators from the locator. Used at app startup.</summary>
    public FileOperationService()
        : this(
            ServiceLocator.Get<IFileSystem>(),
            ServiceLocator.Get<IRecycleBin>(),
            ServiceLocator.Get<UndoService>(),
            ServiceLocator.Get<OperationTracker>(),
            ServiceLocator.IsRegistered<ILogger>() ? ServiceLocator.Get<ILogger>() : NullLogger.Instance) {
    }


    // --- Single-item ops -----------------------------------------------

    public void Copy(string source, string destination, bool overwrite = false) {
        using var _ = _undo.BeginOperation();
        if (_fs.DirectoryExists(source)) {
            _fs.CopyDirectory(source, destination, overwrite);
        } else if (_fs.FileExists(source)) {
            _fs.CopyFile(source, destination, overwrite);
        } else {
            throw new FileNotFoundException("Source not found", source);
        }
        _log.Info($"Copy: {source} -> {destination} (overwrite={overwrite})");
        _undo.Push(new CreateAction(_bin, destination));
    }

    public void Move(string source, string destination) {
        using var _ = _undo.BeginOperation();
        if (!_fs.FileExists(source) && !_fs.DirectoryExists(source)) {
            throw new FileNotFoundException("Source not found", source);
        }
        _fs.MoveEntry(source, destination);
        _log.Info($"Move: {source} -> {destination}");
        _undo.Push(new MoveAction(_fs, source, destination));
    }

    /// <summary>Sends the item to the recycle bin so it remains restorable via Ctrl+Z.</summary>
    public void Delete(string path) {
        using var _ = _undo.BeginOperation();
        var handle = _bin.Send(path);
        _log.Info($"Delete (recycle): {path}");
        _undo.Push(new DeleteAction(_bin, handle));
    }

    /// <summary>
    /// Skips the recycle bin and removes the item from disk. Not undoable —
    /// clears the existing undo stack so the user can't accidentally Ctrl+Z
    /// past a permanent action and think it worked.
    /// </summary>
    public void PermanentDelete(string path) {
        using var _ = _undo.BeginOperation();
        if (_fs.DirectoryExists(path)) {
            _fs.DeleteDirectory(path, recursive: true);
        } else if (_fs.FileExists(path)) {
            _fs.DeleteFile(path);
        } else {
            throw new FileNotFoundException("Path not found", path);
        }
        _log.Warn($"Permanent delete: {path}");
        _undo.Clear();
    }

    public void Rename(string path, string newName) {
        if (string.IsNullOrWhiteSpace(newName)) {
            throw new ArgumentException("New name cannot be empty", nameof(newName));
        }
        using var _ = _undo.BeginOperation();
        string oldName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _fs.Rename(path, newName);
        string parent = Path.GetDirectoryName(path) ?? "";
        string newPath = Path.Combine(parent, newName);
        _log.Info($"Rename: {path} -> {newName}");
        _undo.Push(new RenameAction(_fs, newPath, oldName));
    }

    public void CreateFolder(string parent, string name) {
        using var _ = _undo.BeginOperation();
        var path = Path.Combine(parent, name);
        _fs.CreateDirectory(path);
        _log.Info($"CreateFolder: {path}");
        _undo.Push(new CreateAction(_bin, path));
    }


    // --- Batch ops (delegate to BatchExecutor) -------------------------

    public IReadOnlyList<BatchItemResult> CopyMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver)
        => _batch.CopyMany(sources, targetFolder, resolver);

    public IReadOnlyList<BatchItemResult> MoveMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver)
        => _batch.MoveMany(sources, targetFolder, resolver);

    public Task<IReadOnlyList<BatchItemResult>> CopyManyAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver,
        CancellationToken ct = default)
        => _batch.CopyManyAsync(sources, targetFolder, resolver, ct);

    public Task<IReadOnlyList<BatchItemResult>> MoveManyAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver,
        CancellationToken ct = default)
        => _batch.MoveManyAsync(sources, targetFolder, resolver, ct);

    public Task<IReadOnlyList<DeleteResult>> DeleteManyAsync(
        IReadOnlyList<string> paths, bool permanent, CancellationToken ct = default)
        => _batch.DeleteManyAsync(paths, permanent, ct);
}
