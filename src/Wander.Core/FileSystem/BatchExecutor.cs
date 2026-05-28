using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Undo;

namespace Wander.Core.FileSystem;

/// <summary>
/// Carries out batch copy / move / delete with conflict resolution and
/// progress reporting. <see cref="FileOperationService"/> owns one of these
/// and forwards every <c>*Many*</c> call here — keeping the heavy logic
/// (conflict loop, composite undo, recycle-vs-permanent branching) in its
/// own class means the service stays a tiny facade and this code can be
/// tested in isolation.
///
/// <para>
/// Sync entry points (<see cref="CopyMany"/> / <see cref="MoveMany"/>) exist
/// for tests and legacy callers; the production code path is async — work
/// runs on the thread pool, reports per-item progress into the shared
/// <see cref="OperationTracker"/>, and is observable in the status bar.
/// </para>
/// </summary>
public sealed class BatchExecutor {
    private readonly IFileSystem _fs;
    private readonly IRecycleBin _bin;
    private readonly UndoService _undo;
    private readonly OperationTracker _tracker;
    private readonly ILogger _log;


    public BatchExecutor(IFileSystem fs, IRecycleBin bin, UndoService undo, OperationTracker tracker, ILogger log) {
        _fs = fs;
        _bin = bin;
        _undo = undo;
        _tracker = tracker;
        _log = log;
    }


    // --- Sync entry points (tests + legacy) ----------------------------

    public IReadOnlyList<BatchItemResult> CopyMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver) {
        return ApplyBatch(sources, targetFolder, isMove: false, resolver, progress: null);
    }

    public IReadOnlyList<BatchItemResult> MoveMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver) {
        return ApplyBatch(sources, targetFolder, isMove: true, resolver, progress: null);
    }


    // --- Async entry points (production) -------------------------------

    public Task<IReadOnlyList<BatchItemResult>> CopyManyAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver,
        CancellationToken ct) {
        return RunBatchAsync(sources, targetFolder, isMove: false, resolver, ct);
    }

    public Task<IReadOnlyList<BatchItemResult>> MoveManyAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver,
        CancellationToken ct) {
        return RunBatchAsync(sources, targetFolder, isMove: true, resolver, ct);
    }

    /// <summary>
    /// Async batch delete. <paramref name="permanent"/> = true bypasses the
    /// recycle bin and clears the undo stack (same semantics as
    /// <see cref="FileOperationService.PermanentDelete"/>).
    /// </summary>
    public async Task<IReadOnlyList<DeleteResult>> DeleteManyAsync(
        IReadOnlyList<string> paths, bool permanent, CancellationToken ct) {
        using var op = _tracker.Begin(permanent ? "Delete permanently" : "Recycle", paths.Count);
        return await Task.Run(
            () => DeleteManyCore(paths, permanent, op, ct),
            ct).ConfigureAwait(false);
    }


    // --- Internals -----------------------------------------------------

    private async Task<IReadOnlyList<BatchItemResult>> RunBatchAsync(
        IReadOnlyList<string> sources, string targetFolder, bool isMove, IConflictResolver resolver,
        CancellationToken ct) {
        using var op = _tracker.Begin(isMove ? "Move" : "Copy", sources.Count);
        return await Task.Run(
            () => ApplyBatch(sources, targetFolder, isMove, resolver, op),
            ct).ConfigureAwait(false);
    }

    private IReadOnlyList<BatchItemResult> ApplyBatch(IReadOnlyList<string> sources, string targetFolder, bool isMove, IConflictResolver resolver, IOperationHandle? progress) {
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
                    progress?.Advance(src);
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

            progress?.Advance(src);
        }

        PushComposite(undoSteps, isMove);
        return results;
    }

    private IReadOnlyList<DeleteResult> DeleteManyCore(
        IReadOnlyList<string> paths, bool permanent, IOperationHandle progress, CancellationToken ct) {
        using var _ = _undo.BeginOperation();

        var results = new List<DeleteResult>(paths.Count);
        var undoSteps = new List<IUndoableAction>(paths.Count);

        foreach (string path in paths) {
            if (ct.IsCancellationRequested) {
                results.Add(new DeleteResult(path, DeleteStatus.Cancelled, null));
                continue;
            }

            try {
                if (permanent) {
                    if (_fs.DirectoryExists(path)) {
                        _fs.DeleteDirectory(path, recursive: true);
                    } else if (_fs.FileExists(path)) {
                        _fs.DeleteFile(path);
                    } else {
                        throw new FileNotFoundException("Path not found", path);
                    }
                    _log.Warn($"Permanent delete: {path}");
                    results.Add(new DeleteResult(path, DeleteStatus.Ok, null));
                } else {
                    var handle = _bin.Send(path);
                    undoSteps.Add(new DeleteAction(_bin, handle));
                    _log.Info($"Delete (recycle): {path}");
                    results.Add(new DeleteResult(path, DeleteStatus.Ok, null));
                }
            } catch (Exception ex) {
                _log.Error($"Delete failed: {path}", ex);
                results.Add(new DeleteResult(path, DeleteStatus.Failed, ex));
            }

            progress.Advance(path);
        }

        if (permanent) {
            // Permanent delete is not undoable — drop any history so users can't
            // Ctrl+Z past it and think it worked.
            _undo.Clear();
        } else {
            PushComposite(undoSteps, isMove: false, verbOverride: "delete");
        }

        return results;
    }

    private void PushComposite(IReadOnlyList<IUndoableAction> steps, bool isMove, string? verbOverride = null) {
        if (steps.Count == 0) {
            return;
        }
        string verb = verbOverride ?? (isMove ? "move" : "copy");
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
            IsReadOnly: false,
            IsSystem: false);
    }
}


// --- Batch result types (top-level so callers don't need to reach into BatchExecutor) ---

public sealed record BatchItemResult(string Source, string FinalDestination, BatchItemStatus Status, Exception? Error);
public enum BatchItemStatus { Ok, Skipped, Replaced, Renamed, Cancelled, Failed }

public sealed record DeleteResult(string Path, DeleteStatus Status, Exception? Error);
public enum DeleteStatus { Ok, Failed, Cancelled }
