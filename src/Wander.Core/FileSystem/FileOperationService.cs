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
/// From outside Core the surface is deliberately the batch API plus
/// <c>RenameMany</c> and <c>CreateFolder</c>: everything a user can do
/// applies to a multi-selection, and a file with sidecars is a group even
/// when one row was selected. The single-item ops are internal — the steps
/// the batch path is built from, kept visible to the tests that pin down
/// their guards, logging and undo records.
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
            ServiceLocator.Get<ILogger>()) {
    }


    // --- Single-item ops (internal — see the note on the class) ---------

    internal void Copy(string source, string destination, bool overwrite = false) {
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

    internal void Move(string source, string destination) {
        GuardDestructive(source);
        using var _ = _undo.BeginOperation();
        if (!_fs.FileExists(source) && !_fs.DirectoryExists(source)) {
            throw new FileNotFoundException("Source not found", source);
        }
        _fs.MoveEntry(source, destination);
        _log.Info($"Move: {source} -> {destination}");
        _undo.Push(new MoveAction(_fs, source, destination));
    }

    /// <summary>Sends the item to the recycle bin so it remains restorable via Ctrl+Z.</summary>
    internal void Delete(string path) {
        GuardDestructive(path);
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
    internal void PermanentDelete(string path) {
        GuardDestructive(path);
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

    internal void Rename(string path, string newName) {
        if (string.IsNullOrWhiteSpace(newName)) {
            throw new ArgumentException("New name cannot be empty", nameof(newName));
        }
        GuardDestructive(path);
        using var _ = _undo.BeginOperation();
        string oldName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _fs.Rename(path, newName);
        string parent = Path.GetDirectoryName(path) ?? "";
        string newPath = Path.Combine(parent, newName);
        _log.Info($"Rename: {path} -> {newName}");
        _undo.Push(new RenameAction(_fs, newPath, oldName));
    }

    /// <summary>
    /// Renames a group of files as one action — a main file together with
    /// its companion sidecars. All-or-nothing: a failure part-way through
    /// puts the already-renamed members back before the exception leaves
    /// this method, so the user never ends up with <c>Ship.png</c> next to
    /// <c>Sprite.png.meta</c>.
    /// </summary>
    /// <param name="renames">Path → new name, main file first.</param>
    public void RenameMany(IReadOnlyList<(string Path, string NewName)> renames) {
        if (renames.Count == 1) {
            Rename(renames[0].Path, renames[0].NewName);

            return;
        }

        foreach (var (path, newName) in renames) {
            if (string.IsNullOrWhiteSpace(newName)) {
                throw new ArgumentException("New name cannot be empty", nameof(renames));
            }
            GuardDestructive(path);
        }

        using var _ = _undo.BeginOperation();
        var steps = new List<IUndoableAction>(renames.Count);
        try {
            foreach (var (path, newName) in renames) {
                string oldName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                _fs.Rename(path, newName);
                string parent = Path.GetDirectoryName(path) ?? "";
                steps.Add(new RenameAction(_fs, Path.Combine(parent, newName), oldName));
                _log.Info($"Rename: {path} -> {newName}");
            }
        } catch {
            Rollback(steps);
            throw;
        }

        _undo.Push(new CompositeAction($"Rename to '{renames[0].NewName}'", steps));
    }


    private void Rollback(IReadOnlyList<IUndoableAction> steps) {
        for (int i = steps.Count - 1; i >= 0; i--) {
            try {
                steps[i].Undo();
            } catch (Exception ex) {
                // Nothing better to do than say so: the group is now split
                // and the user has to see which half moved.
                _log.Error("Rename rollback failed", ex);
            }
        }
    }


    public void CreateFolder(string parent, string name) {
        using var _ = _undo.BeginOperation();
        var path = Path.Combine(parent, name);
        _fs.CreateDirectory(path);
        _log.Info($"CreateFolder: {path}");
        _undo.Push(new CreateAction(_bin, path));
    }


    private static void GuardDestructive(string path) {
        if (SystemPathGuard.IsProtected(path, out string reason)) {
            throw new IOException(reason);
        }
    }


    // --- Batch ops (delegate to BatchExecutor) -------------------------

    internal IReadOnlyList<BatchItemResult> CopyMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver)
        => _batch.CopyMany(sources, targetFolder, resolver);

    internal IReadOnlyList<BatchItemResult> MoveMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver)
        => _batch.MoveMany(sources, targetFolder, resolver);

    public Task<IReadOnlyList<BatchItemResult>> CopyManyAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver,
        CancellationToken ct = default)
        => _batch.CopyManyAsync(sources, targetFolder, resolver, ct);

    public Task<IReadOnlyList<BatchItemResult>> MoveManyAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver,
        CancellationToken ct = default)
        => _batch.MoveManyAsync(sources, targetFolder, resolver, ct);

    // Group overloads: a main file and its companions count as one item for
    // conflicts, for progress and for the status bar. See <see cref="BatchGroup"/>.

    public Task<IReadOnlyList<BatchItemResult>> CopyManyAsync(
        IReadOnlyList<BatchGroup> groups, string targetFolder, IConflictResolver resolver,
        CancellationToken ct = default)
        => _batch.CopyManyAsync(groups, targetFolder, resolver, ct);

    public Task<IReadOnlyList<BatchItemResult>> MoveManyAsync(
        IReadOnlyList<BatchGroup> groups, string targetFolder, IConflictResolver resolver,
        CancellationToken ct = default)
        => _batch.MoveManyAsync(groups, targetFolder, resolver, ct);

    public Task<IReadOnlyList<DeleteResult>> DeleteManyAsync(
        IReadOnlyList<string> paths, bool permanent, CancellationToken ct = default)
        => _batch.DeleteManyAsync(paths, permanent, ct);
}
