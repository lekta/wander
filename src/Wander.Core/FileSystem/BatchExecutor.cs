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
internal sealed class BatchExecutor {
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
    // The path-list overloads treat every path as a group of one, which is
    // what a caller that knows nothing about companions means anyway.

    public IReadOnlyList<BatchItemResult> CopyMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver) {
        return CopyMany(AsGroups(sources), targetFolder, resolver);
    }

    public IReadOnlyList<BatchItemResult> MoveMany(IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver) {
        return MoveMany(AsGroups(sources), targetFolder, resolver);
    }

    public IReadOnlyList<BatchItemResult> CopyMany(IReadOnlyList<BatchGroup> groups, string targetFolder, IConflictResolver resolver) {
        return ApplyBatch(groups, targetFolder, isMove: false, resolver, progress: null);
    }

    public IReadOnlyList<BatchItemResult> MoveMany(IReadOnlyList<BatchGroup> groups, string targetFolder, IConflictResolver resolver) {
        return ApplyBatch(groups, targetFolder, isMove: true, resolver, progress: null);
    }


    // --- Async entry points (production) -------------------------------

    public Task<IReadOnlyList<BatchItemResult>> CopyManyAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver,
        CancellationToken ct) {
        return RunBatchAsync(AsGroups(sources), targetFolder, isMove: false, resolver, ct);
    }

    public Task<IReadOnlyList<BatchItemResult>> MoveManyAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver,
        CancellationToken ct) {
        return RunBatchAsync(AsGroups(sources), targetFolder, isMove: true, resolver, ct);
    }

    public Task<IReadOnlyList<BatchItemResult>> CopyManyAsync(
        IReadOnlyList<BatchGroup> groups, string targetFolder, IConflictResolver resolver,
        CancellationToken ct) {
        return RunBatchAsync(groups, targetFolder, isMove: false, resolver, ct);
    }

    public Task<IReadOnlyList<BatchItemResult>> MoveManyAsync(
        IReadOnlyList<BatchGroup> groups, string targetFolder, IConflictResolver resolver,
        CancellationToken ct) {
        return RunBatchAsync(groups, targetFolder, isMove: true, resolver, ct);
    }


    private static IReadOnlyList<BatchGroup> AsGroups(IReadOnlyList<string> paths) {
        return paths.Select(BatchGroup.Single).ToList();
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
        IReadOnlyList<BatchGroup> groups, string targetFolder, bool isMove, IConflictResolver resolver,
        CancellationToken ct) {
        // Progress counts groups, not files: the user dragged three photos,
        // not three photos and three sidecars.
        using var op = _tracker.Begin(isMove ? "Move" : "Copy", groups.Count);
        return await Task.Run(
            () => ApplyBatch(groups, targetFolder, isMove, resolver, op, ct),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The batch loop. One iteration per <see cref="BatchGroup"/>, and one
    /// result per group: a companion is not a thing the user counted, so it
    /// is not a thing the status bar counts either.
    ///
    /// <para>
    /// A group's members are applied main file first. If a companion fails
    /// after the main file went through, the group is reported failed and
    /// the successful members stay applied — with their undo steps in the
    /// composite, so <c>Ctrl+Z</c> still puts everything back.
    /// </para>
    /// </summary>
    private IReadOnlyList<BatchItemResult> ApplyBatch(
        IReadOnlyList<BatchGroup> groups, string targetFolder, bool isMove, IConflictResolver resolver,
        IOperationHandle? progress, CancellationToken ct = default) {
        using var _ = _undo.BeginOperation();

        var plans = groups.Select(g => Plan(g, targetFolder)).ToList();

        // Counted in groups so the "N conflicts" the user is told about is
        // the number of decisions they are about to be asked for.
        int conflictCount = plans.Count(p => p.Members.Any(m => Exists(m.Dest)));
        ConflictResolution? batchOverride = null;

        if (conflictCount > 0) {
            batchOverride = resolver.StartBatch(conflictCount);
            if (batchOverride == ConflictResolution.Cancel) {
                _log.Info($"Batch {(isMove ? "move" : "copy")} cancelled by user before start ({plans.Count} items, {conflictCount} conflicts)");
                return plans.Select(p => Cancelled(p)).ToList();
            }
        }

        var results = new List<BatchItemResult>(plans.Count);
        var undoSteps = new List<IUndoableAction>(plans.Count);
        int appliedCount = 0;

        foreach (var plan in plans) {
            // The progress dialog's Cancel cancels this token; already-applied
            // items stay applied (and undoable), the rest are marked Cancelled.
            if (ct.IsCancellationRequested) {
                results.Add(Cancelled(plan));
                continue;
            }

            // One question per group, even when several of its files collide.
            // The dialog describes the main file — that is the thing the user
            // dragged, and the sidecar's own name would only puzzle them.
            var current = plan;
            bool exists = current.Members.Any(m => Exists(m.Dest));
            ConflictResolution? choice = exists
                ? (batchOverride ?? resolver.Resolve(BuildInfo(current.Members[0].Source, current.Members[0].Dest)))
                : null;

            var statusKind = BatchItemStatus.Ok;
            switch (choice) {
                case ConflictResolution.Cancel:
                    results.Add(Cancelled(current));
                    foreach (var rest in plans.Skip(results.Count)) {
                        results.Add(Cancelled(rest));
                    }
                    PushComposite(undoSteps, isMove, appliedCount);
                    return results;
                case ConflictResolution.Skip:
                    results.Add(new BatchItemResult(current.Members[0].Source, current.Members[0].Dest, BatchItemStatus.Skipped, null));
                    progress?.Advance(current.Members[0].Source);
                    continue;
                case ConflictResolution.Rename:
                    current = WithUniqueNames(current);
                    statusKind = BatchItemStatus.Renamed;
                    break;
                case ConflictResolution.Replace:
                    statusKind = BatchItemStatus.Replaced;
                    break;
                case null:
                    break;
            }

            Exception? failure = null;
            bool anyApplied = false;
            foreach (var (src, dest) in current.Members) {
                try {
                    // System-path guard: moving a protected path away is as
                    // destructive as deleting it; replacing a protected target
                    // would recycle a system file.
                    if (isMove && SystemPathGuard.IsProtected(src, out string srcReason)) {
                        throw new IOException(srcReason);
                    }
                    if (choice == ConflictResolution.Replace && Exists(dest)) {
                        if (SystemPathGuard.IsProtected(dest, out string destReason)) {
                            throw new IOException(destReason);
                        }
                        // "Everything undoable" pillar: the replaced target goes
                        // to the recycle bin, never into oblivion. Its restore
                        // step lands in the composite before the main action, so
                        // undo (which runs in reverse) first un-moves/un-copies,
                        // then brings the old target back. If the op below fails
                        // the target is still recoverable via the same step.
                        undoSteps.Add(new DeleteAction(_bin, _bin.Send(dest)));
                    }

                    ApplyOne(src, dest, isMove);
                    undoSteps.Add(isMove
                        ? new MoveAction(_fs, src, dest)
                        : new CreateAction(_bin, dest));
                    anyApplied = true;
                    _log.Info($"{(isMove ? "Move" : "Copy")}: {src} -> {dest} [{statusKind}]");
                } catch (Exception ex) {
                    failure ??= ex;
                    _log.Error($"{(isMove ? "Move" : "Copy")} failed: {src} -> {dest}", ex);
                }
            }

            var (primarySrc, primaryDest) = current.Members[0];
            if (failure is null) {
                results.Add(new BatchItemResult(primarySrc, primaryDest, statusKind, null));
                appliedCount++;
            } else {
                results.Add(new BatchItemResult(primarySrc, primaryDest, BatchItemStatus.Failed, failure));
                if (anyApplied) {
                    // Part of the group moved. Nothing is lost — the composite
                    // undo covers it — but the user has to be told, hence the
                    // whole group reads as failed rather than "mostly fine".
                    appliedCount++;
                }
            }

            progress?.Advance(primarySrc);
        }

        PushComposite(undoSteps, isMove, appliedCount);
        return results;
    }

    /// <summary>Source → destination for every file in the group, main file first.</summary>
    private static GroupPlan Plan(BatchGroup group, string targetFolder) {
        var members = group.All
            .Select(src => (Source: src, Dest: Path.Combine(targetFolder, NameOf(src))))
            .ToList();

        return new GroupPlan(members);
    }

    /// <summary>
    /// The group under a free name. The main file gets the usual
    /// "name (1).ext" treatment; the companions follow it by substituting
    /// the part of their name they share with the main file, so
    /// <c>Sprite.png</c> → <c>Sprite (1).png</c> takes
    /// <c>Sprite.png.meta</c> → <c>Sprite (1).png.meta</c> and
    /// <c>IMG.CR2</c> → <c>IMG (1).CR2</c> takes <c>IMG.xmp</c> →
    /// <c>IMG (1).xmp</c>. No knowledge of either format is needed for that.
    /// </summary>
    private GroupPlan WithUniqueNames(GroupPlan plan) {
        var (primarySrc, primaryDest) = plan.Members[0];
        string renamedDest = GenerateUniqueName(primaryDest);

        string oldName = NameOf(primaryDest);
        string newName = NameOf(renamedDest);
        string dir = Path.GetDirectoryName(renamedDest) ?? "";

        var members = new List<(string Source, string Dest)>(plan.Members.Count) { (primarySrc, renamedDest) };
        foreach (var (src, dest) in plan.Members.Skip(1)) {
            string companion = NameOf(dest);
            string? moved = Rebase(companion, oldName, newName)
                ?? Rebase(companion, Path.GetFileNameWithoutExtension(oldName), Path.GetFileNameWithoutExtension(newName));
            members.Add((src, Path.Combine(dir, moved ?? NameOf(GenerateUniqueName(dest)))));
        }

        return new GroupPlan(members);
    }

    /// <summary>
    /// <paramref name="name"/> with a leading <paramref name="oldPrefix"/>
    /// swapped for <paramref name="newPrefix"/>, or null when it doesn't
    /// start with that prefix. The remainder has to begin with a dot, so
    /// "IMGX.xmp" is never treated as a variant of "IMG".
    /// </summary>
    private static string? Rebase(string name, string oldPrefix, string newPrefix) {
        if (oldPrefix.Length == 0 || !name.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }
        string tail = name[oldPrefix.Length..];

        return tail.StartsWith('.') ? newPrefix + tail : null;
    }

    private static BatchItemResult Cancelled(GroupPlan plan) {
        return new BatchItemResult(plan.Members[0].Source, plan.Members[0].Dest, BatchItemStatus.Cancelled, null);
    }

    private static string NameOf(string path) {
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
                if (SystemPathGuard.IsProtected(path, out string guardReason)) {
                    throw new IOException(guardReason);
                }

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
            PushComposite(undoSteps, isMove: false, undoSteps.Count, verbOverride: "delete");
        }

        return results;
    }

    private void PushComposite(IReadOnlyList<IUndoableAction> steps, bool isMove, int itemCount, string? verbOverride = null) {
        if (steps.Count == 0) {
            return;
        }
        string verb = verbOverride ?? (isMove ? "move" : "copy");
        // The user thinks in items, not undo steps (a Replace contributes two
        // steps: restore-target + un-move); describe single items by their
        // main — always last — action.
        string desc = itemCount == 1
            ? steps[^1].Description
            : $"{verb} of {itemCount} items";
        _undo.Push(steps.Count == 1 ? steps[0] : new CompositeAction(desc, steps));
    }

    private void ApplyOne(string src, string dest, bool isMove) {
        // A Replace conflict never reaches here with the target still in
        // place — ApplyBatch recycles it first — so plain no-overwrite
        // semantics are enough for both branches.
        if (isMove) {
            _fs.MoveEntry(src, dest);
            return;
        }

        if (_fs.DirectoryExists(src)) {
            _fs.CopyDirectory(src, dest, overwrite: false);
        } else {
            _fs.CopyFile(src, dest, overwrite: false);
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
            IsSystem: false,
            LinksToDirectory: false);
    }


    private sealed record GroupPlan(IReadOnlyList<(string Source, string Dest)> Members);
}


// --- Batch result types (top-level so callers don't need to reach into BatchExecutor) ---

public sealed record BatchItemResult(string Source, string FinalDestination, BatchItemStatus Status, Exception? Error);
public enum BatchItemStatus { Ok, Skipped, Replaced, Renamed, Cancelled, Failed }

public sealed record DeleteResult(string Path, DeleteStatus Status, Exception? Error);
public enum DeleteStatus { Ok, Failed, Cancelled }
