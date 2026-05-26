using Wander.Core.Logging;
using Wander.Core.Undo;

namespace Wander.Core.FileSystem;

/// <summary>
/// Single entry point for every file-modifying operation in Wander.
/// Registered as a singleton in the service locator so the same instance —
/// and the same undo stack — sees every call site (VM, drop handlers,
/// future scripting, etc.).
///
/// <para>
/// Each successful op also lands as an <see cref="IUndoableAction"/> on the
/// shared <see cref="UndoService"/>. Pure read paths still live on
/// <see cref="IFileSystem"/> and bypass this service.
/// </para>
///
/// <para>
/// Async note: today every method is synchronous. When we start moving
/// large folders off the UI thread, signatures will gain Task-returning
/// overloads and the undo guard (<see cref="UndoService.BeginOperation"/>)
/// will hold for the whole async lifetime.
/// </para>
/// </summary>
public sealed class FileOperationService {
    private readonly IFileSystem _fs;
    private readonly IRecycleBin _bin;
    private readonly UndoService _undo;
    private readonly ILogger _log;


    /// <summary>Full ctor — used by tests and the production registration in PlatformBootstrapper.</summary>
    public FileOperationService(IFileSystem fs, IRecycleBin bin, UndoService undo, ILogger log) {
        _fs = fs;
        _bin = bin;
        _undo = undo;
        _log = log;
    }

    /// <summary>Convenience ctor that pulls collaborators from the locator. Used at app startup.</summary>
    public FileOperationService()
        : this(
            ServiceLocator.Get<IFileSystem>(),
            ServiceLocator.Get<IRecycleBin>(),
            ServiceLocator.Get<UndoService>(),
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


    // --- Batch ops with conflict resolution ----------------------------

    public sealed record BatchItemResult(string Source, string FinalDestination, BatchItemStatus Status, Exception? Error);

    public enum BatchItemStatus { Ok, Skipped, Replaced, Renamed, Cancelled, Failed }

    public IReadOnlyList<BatchItemResult> CopyMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver) {
        return ApplyBatch(sources, targetFolder, isMove: false, resolver);
    }

    public IReadOnlyList<BatchItemResult> MoveMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver) {
        return ApplyBatch(sources, targetFolder, isMove: true, resolver);
    }


    private IReadOnlyList<BatchItemResult> ApplyBatch(IReadOnlyList<string> sources, string targetFolder, bool isMove, IConflictResolver resolver) {
        using var _ = _undo.BeginOperation();

        var pairs = new List<(string src, string dest)>();
        foreach (string src in sources) {
            string name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            pairs.Add((src, Path.Combine(targetFolder, name)));
        }

        int conflictCount = pairs.Count(p => Exists(p.dest));
        ConflictResolution? batchOverride = null;

        if (conflictCount > 0) {
            batchOverride = resolver.StartBatch(conflictCount);
            if (batchOverride == ConflictResolution.Cancel) {
                _log.Info($"Batch {(isMove ? "move" : "copy")} cancelled by user before start ({pairs.Count} items, {conflictCount} conflicts)");
                return pairs.Select(p => new BatchItemResult(p.src, p.dest, BatchItemStatus.Cancelled, null)).ToList();
            }
        }

        var results = new List<BatchItemResult>(pairs.Count);
        var undoSteps = new List<IUndoableAction>(pairs.Count);

        foreach (var (src, originalDest) in pairs) {
            string dest = originalDest;
            BatchItemStatus statusKind = BatchItemStatus.Ok;
            bool exists = Exists(dest);

            ConflictResolution? choice = exists
                ? (batchOverride ?? resolver.Resolve(BuildInfo(src, dest)))
                : null;

            switch (choice) {
                case ConflictResolution.Cancel:
                    results.Add(new BatchItemResult(src, dest, BatchItemStatus.Cancelled, null));
                    foreach (var (rsrc, rdest) in pairs.Skip(results.Count)) {
                        results.Add(new BatchItemResult(rsrc, rdest, BatchItemStatus.Cancelled, null));
                    }
                    PushComposite(undoSteps, isMove);
                    return results;
                case ConflictResolution.Skip:
                    results.Add(new BatchItemResult(src, dest, BatchItemStatus.Skipped, null));
                    continue;
                case ConflictResolution.Rename:
                    dest = GenerateUniqueName(dest);
                    statusKind = BatchItemStatus.Renamed;
                    break;
                case ConflictResolution.Replace:
                    statusKind = BatchItemStatus.Replaced;
                    break;
                case null:
                    break;
            }

            try {
                ApplyOne(src, dest, isMove, allowOverwrite: choice == ConflictResolution.Replace);
                results.Add(new BatchItemResult(src, dest, statusKind, null));
                undoSteps.Add(isMove
                    ? new MoveAction(_fs, src, dest)
                    : new CreateAction(_bin, dest));
                _log.Info($"{(isMove ? "Move" : "Copy")}: {src} -> {dest} [{statusKind}]");
            } catch (Exception ex) {
                results.Add(new BatchItemResult(src, dest, BatchItemStatus.Failed, ex));
                _log.Error($"{(isMove ? "Move" : "Copy")} failed: {src} -> {dest}", ex);
            }
        }

        PushComposite(undoSteps, isMove);
        return results;
    }

    private void PushComposite(IReadOnlyList<IUndoableAction> steps, bool isMove) {
        if (steps.Count == 0) {
            return;
        }
        string verb = isMove ? "move" : "copy";
        string desc = steps.Count == 1
            ? steps[0].Description
            : $"{verb} of {steps.Count} items";
        _undo.Push(steps.Count == 1 ? steps[0] : new CompositeAction(desc, steps));
    }

    private void ApplyOne(string src, string dest, bool isMove, bool allowOverwrite) {
        if (isMove) {
            if (allowOverwrite) {
                // .NET's Move doesn't support overwrite-for-folders; clear target first.
                if (_fs.FileExists(dest)) {
                    _fs.DeleteFile(dest);
                } else if (_fs.DirectoryExists(dest)) {
                    _fs.DeleteDirectory(dest, recursive: true);
                }
            }
            _fs.MoveEntry(src, dest);
            return;
        }

        if (_fs.DirectoryExists(src)) {
            _fs.CopyDirectory(src, dest, overwrite: allowOverwrite);
        } else {
            _fs.CopyFile(src, dest, overwrite: allowOverwrite);
        }
    }

    private bool Exists(string path) => _fs.FileExists(path) || _fs.DirectoryExists(path);

    private string GenerateUniqueName(string desiredPath) {
        string dir = Path.GetDirectoryName(desiredPath) ?? "";
        string nameNoExt = Path.GetFileNameWithoutExtension(desiredPath);
        string ext = Path.GetExtension(desiredPath);
        int i = 1;
        while (true) {
            string candidate = Path.Combine(dir, $"{nameNoExt} ({i}){ext}");
            if (!Exists(candidate)) {
                return candidate;
            }
            i++;
        }
    }

    private FileConflictInfo BuildInfo(string src, string dest) {
        var srcEntry = _fs.GetEntry(src) ?? Unknown(src);
        var dstEntry = _fs.GetEntry(dest) ?? Unknown(dest);
        return new FileConflictInfo(srcEntry, dstEntry);
    }

    private static FileSystemEntry Unknown(string path) {
        return new FileSystemEntry(
            Name: Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            FullPath: path,
            Kind: EntryKind.File,
            Size: null,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false);
    }
}
