using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Undo;

namespace Wander.Core.FileSystem;

/// <summary>
/// Carries out batch copy / move / delete with conflict resolution and
/// progress reporting. <see cref="FileOperationService"/> owns one of these
/// and forwards every <c>*Many*</c> call here - keeping the heavy logic
/// (conflict loop, composite undo, recycle-vs-permanent branching) in its
/// own class means the service stays a tiny facade and this code can be
/// tested in isolation.
///
/// <para>
/// Sync entry points (<see cref="CopyMany"/> / <see cref="MoveMany"/>) exist
/// for tests and legacy callers; the production code path is async - work
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
    /// Inside a group every member is its own file: it collides on its own,
    /// is asked about on its own and answers for itself - a sidecar's
    /// content changes independently of its main file's. The one thing that
    /// binds them is the name: a main file that goes under a new name takes
    /// its companions along (see <see cref="ApplyGroup"/>).
    /// </para>
    /// </summary>
    private IReadOnlyList<BatchItemResult> ApplyBatch(
        IReadOnlyList<BatchGroup> groups, string targetFolder, bool isMove, IConflictResolver resolver,
        IOperationHandle? progress, CancellationToken ct = default) {
        using var _ = _undo.BeginOperation();

        var plans = groups.Select(g => Plan(g, targetFolder)).ToList();
        var run = new Run(isMove, resolver, plans.Count);

        // An item already where it is being sent is not a collision, and
        // nothing worth a question: copying it there is how a duplicate is
        // made (keep both), moving it there has nothing to do (skip).
        foreach (var member in plans.SelectMany(p => p.Members)) {
            if (string.Equals(member.Source, member.Dest, StringComparison.OrdinalIgnoreCase)) {
                member.Choice = isMove ? ConflictResolution.Skip : ConflictResolution.Rename;
            }
        }

        // Every collision is known before anything moves, and the user is
        // asked about all of them at once - see IConflictResolver. A Cancel
        // there costs nothing: nothing has been applied yet.
        if (!AskUpFront(plans, run)) {
            _log.Info($"Batch {(isMove ? "move" : "copy")} cancelled by user before start ({plans.Count} items)");
            return plans.Select(p => Cancelled(p)).ToList();
        }

        var results = new List<BatchItemResult>(plans.Count);
        int appliedCount = 0;

        foreach (var plan in plans) {
            // The progress dialog's Cancel cancels this token, and a Cancel
            // from a late question sets the run's flag; either way the
            // already-applied items stay applied (and undoable), the rest
            // are marked Cancelled.
            if (ct.IsCancellationRequested || run.Cancelled) {
                results.Add(Cancelled(plan));
                continue;
            }

            var (result, applied) = ApplyGroup(plan, run);
            results.Add(result);
            if (applied) {
                appliedCount++;
            }

            progress?.Advance(plan.Members[0].Source);
        }

        PushComposite(run.UndoSteps, isMove, appliedCount);
        return results;
    }

    /// <summary>
    /// Asks about every member that collides, all in one call, and writes
    /// the answers back onto the members - found by source path, which is
    /// also how a pair inside a merged folder is matched later.
    /// </summary>
    /// <returns>False when the user backed out of the whole batch.</returns>
    private bool AskUpFront(IReadOnlyList<GroupPlan> plans, Run run) {
        var asked = plans
            .SelectMany(p => p.Members)
            .Where(m => m.Choice is null && Exists(m.Dest))
            .ToList();
        if (asked.Count == 0) {
            return true;
        }

        if (!Ask(asked.Select(m => BuildInfo(m.Source, m.Dest, run.IsMove)).ToList(), run)) {
            return false;
        }
        foreach (var member in asked) {
            member.Choice = run.AnswerFor(member.Source)
                ?? throw new InvalidOperationException($"The conflict resolver left {member.Source} unanswered.");
        }

        return true;
    }

    /// <summary>
    /// One group: the main file first, then its companions. The main file's
    /// answer decides the names - renamed, it takes every companion that is
    /// not skipped along under the matching new name (<c>Sprite (1).png</c>
    /// with <c>Sprite (1).png.meta</c>), whatever the companion answered: a
    /// sidecar belongs to the file it is named after, and the file at the
    /// old name is somebody else's now. A companion answered "keep both" on
    /// its own goes under the name it would have had - orphaned, but that is
    /// what was asked.
    ///
    /// <para>
    /// The group's status is the main file's; Failed when any member failed,
    /// with the successful members left applied - the composite undo covers
    /// them, and the user has to be told rather than shown "mostly fine".
    /// </para>
    /// </summary>
    private (BatchItemResult Result, bool Applied) ApplyGroup(GroupPlan plan, Run run) {
        var primary = plan.Members[0];
        string[]? renamed = null;
        Exception? failure = null;
        bool anyApplied = false;
        var primaryStatus = BatchItemStatus.Ok;
        string primaryDest = primary.Dest;

        for (int m = 0; m < plan.Members.Count; m++) {
            var member = plan.Members[m];
            if (member.Choice is null && Exists(member.Dest)) {
                // A target that landed after the check up front: nobody was
                // asked about it, so ask now - about this one alone.
                member.Choice = AskLate(member.Source, member.Dest, run);
                if (run.Cancelled) {
                    return (Cancelled(plan), anyApplied);
                }
            }

            var choice = Normalize(member.Choice, member.Source, member.Dest);
            string dest = member.Dest;
            bool follows = m > 0 && primary.Choice == ConflictResolution.Rename && choice != ConflictResolution.Skip;
            if (choice == ConflictResolution.Rename || follows) {
                renamed ??= UniqueNames(plan);
                dest = renamed[m];
                choice = ConflictResolution.Rename;
            }

            var outcome = ApplyEntry(member.Source, dest, choice, run);
            anyApplied |= outcome.Applied;
            failure ??= outcome.Failure;
            if (m == 0) {
                primaryStatus = outcome.Status;
                primaryDest = dest;
            }
            if (run.Cancelled) {
                return (Cancelled(plan), anyApplied);
            }
        }

        return failure is null
            ? (new BatchItemResult(primary.Source, primaryDest, primaryStatus, null), anyApplied)
            : (new BatchItemResult(primary.Source, primaryDest, BatchItemStatus.Failed, failure), anyApplied);
    }

    /// <summary>
    /// One file or folder to its destination under one answer. Rename
    /// arrives with the free name already chosen by the caller; Merge walks
    /// the two folders; Replace sends the target to the bin first. A failure
    /// is reported, not thrown: the rest of the batch goes on.
    /// </summary>
    private Outcome ApplyEntry(string src, string dest, ConflictResolution? choice, Run run) {
        if (choice == ConflictResolution.Skip) {
            return new Outcome(BatchItemStatus.Skipped, false, null);
        }

        try {
            // System-path guard: moving a protected path away is as
            // destructive as deleting it; replacing a protected target
            // would recycle a system file.
            if (run.IsMove && SystemPathGuard.IsProtected(src, out string srcReason)) {
                throw new IOException(srcReason);
            }

            if (choice == ConflictResolution.Merge) {
                return MergeFolder(src, dest, run);
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
                run.UndoSteps.Add(new DeleteAction(_bin, _bin.Send(dest)));
            }

            ApplyOne(src, dest, run.IsMove);
            run.UndoSteps.Add(run.IsMove
                ? new MoveAction(_fs, src, dest)
                : new CreateAction(_bin, dest));
            var status = choice switch {
                ConflictResolution.Replace => BatchItemStatus.Replaced,
                ConflictResolution.Rename => BatchItemStatus.Renamed,
                _ => BatchItemStatus.Ok,
            };
            _log.Info($"{run.Verb}: {src} -> {dest} [{status}]");

            return new Outcome(status, true, null);
        } catch (Exception ex) {
            _log.Error($"{run.Verb} failed: {src} -> {dest}", ex);

            return new Outcome(BatchItemStatus.Failed, false, ex);
        }
    }

    /// <summary>
    /// The contents of one folder into another of the same name. Every
    /// entry lands under its own name in the existing folder: a free name
    /// simply crosses over, a taken one has its answer - the window walked
    /// the tree and asked about every collision - or gets asked now. Folders
    /// taken on both sides merge in turn. After a move that took everything,
    /// the emptied source folder goes to the bin, so <c>Ctrl+Z</c> has it
    /// back before the files return into it.
    /// </summary>
    private Outcome MergeFolder(string src, string dest, Run run) {
        Exception? failure = null;
        bool anyApplied = false;

        foreach (var entry in _fs.Enumerate(src)) {
            if (run.Cancelled) {
                break;
            }

            string childDest = Path.Combine(dest, entry.Name);
            ConflictResolution? choice = null;
            if (Exists(childDest)) {
                choice = Normalize(run.AnswerFor(entry.FullPath) ?? AskLate(entry.FullPath, childDest, run), entry.FullPath, childDest);
                if (run.Cancelled) {
                    break;
                }
                if (choice == ConflictResolution.Rename) {
                    childDest = GenerateUniqueName(childDest);
                }
            }

            var outcome = ApplyEntry(entry.FullPath, childDest, choice, run);
            anyApplied |= outcome.Applied;
            failure ??= outcome.Failure;
        }

        if (run.IsMove && !run.Cancelled && failure is null && _fs.Enumerate(src).Count == 0) {
            run.UndoSteps.Add(new DeleteAction(_bin, _bin.Send(src)));
            anyApplied = true;
        }
        _log.Info($"{run.Verb}: {src} -> {dest} [Merged]");

        return new Outcome(BatchItemStatus.Merged, anyApplied, failure);
    }

    /// <summary>
    /// Merge is an answer for two folders; on anything else it means what
    /// "keep both" means for a file. Nothing else needs translating.
    /// </summary>
    private ConflictResolution? Normalize(ConflictResolution? choice, string src, string dest) {
        if (choice == ConflictResolution.Merge && !(_fs.DirectoryExists(src) && _fs.DirectoryExists(dest))) {
            return ConflictResolution.Rename;
        }

        return choice;
    }

    /// <summary>
    /// Puts the question to the resolver and keeps every answer it gave,
    /// nested ones included, by source path.
    /// </summary>
    /// <returns>False when the user backed out; the run is marked cancelled.</returns>
    private bool Ask(IReadOnlyList<FileConflictInfo> infos, Run run) {
        var answers = run.Resolver.ResolveAll(new ConflictRequest(infos, run.ItemCount));
        if (answers is null || answers.Any(a => a.Resolution == ConflictResolution.Cancel)) {
            run.Cancelled = true;

            return false;
        }

        foreach (var answer in answers) {
            run.Answers[answer.Conflict.Source.FullPath] = answer.Resolution;
        }

        return true;
    }

    /// <summary>
    /// A collision nobody was asked about: a target that landed after the
    /// check up front, or a name inside a merged folder the resolver did not
    /// walk (a scripted one does not). Asked now, about this one alone.
    /// </summary>
    private ConflictResolution? AskLate(string src, string dest, Run run) {
        return Ask(new[] { BuildInfo(src, dest, run.IsMove) }, run) ? run.AnswerFor(src) : null;
    }

    /// <summary>Source -> destination for every file in the group, main file first.</summary>
    private static GroupPlan Plan(BatchGroup group, string targetFolder) {
        var members = group.All
            .Select(src => new MemberPlan(src, Path.Combine(targetFolder, NameOf(src))))
            .ToList();

        return new GroupPlan(members);
    }

    /// <summary>
    /// Every member's destination under a free name, main file first. The
    /// main file gets the usual "name (1).ext" treatment; the companions
    /// follow it by substituting the part of their name they share with the
    /// main file, so <c>Sprite.png</c> -> <c>Sprite (1).png</c> takes
    /// <c>Sprite.png.meta</c> -> <c>Sprite (1).png.meta</c> and
    /// <c>IMG.CR2</c> -> <c>IMG (1).CR2</c> takes <c>IMG.xmp</c> ->
    /// <c>IMG (1).xmp</c>. No knowledge of either format is needed for that.
    /// </summary>
    private string[] UniqueNames(GroupPlan plan) {
        string primaryDest = plan.Members[0].Dest;
        string renamedDest = GenerateUniqueName(primaryDest);

        string oldName = NameOf(primaryDest);
        string newName = NameOf(renamedDest);
        string dir = Path.GetDirectoryName(renamedDest) ?? "";

        var dests = new string[plan.Members.Count];
        dests[0] = renamedDest;
        for (int m = 1; m < plan.Members.Count; m++) {
            string companion = NameOf(plan.Members[m].Dest);
            string? moved = Rebase(companion, oldName, newName)
                ?? Rebase(companion, Path.GetFileNameWithoutExtension(oldName), Path.GetFileNameWithoutExtension(newName));
            dests[m] = Path.Combine(dir, moved ?? NameOf(GenerateUniqueName(plan.Members[m].Dest)));
        }

        return dests;
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
            // Permanent delete is not undoable - drop any history so users can't
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
        // main - always last - action.
        string desc = itemCount == 1
            ? steps[^1].Description
            : $"{verb} of {itemCount} items";
        _undo.Push(steps.Count == 1 ? steps[0] : new CompositeAction(desc, steps));
    }

    private void ApplyOne(string src, string dest, bool isMove) {
        // A Replace conflict never reaches here with the target still in
        // place - ApplyEntry recycles it first - so plain no-overwrite
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

    private FileConflictInfo BuildInfo(string src, string dest, bool isMove) {
        var srcEntry = _fs.GetEntry(src) ?? Unknown(src);
        var dstEntry = _fs.GetEntry(dest) ?? Unknown(dest);

        return new FileConflictInfo(srcEntry, dstEntry, isMove);
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


    /// <summary>One file of a group and what was decided about it.</summary>
    private sealed class MemberPlan {
        public MemberPlan(string source, string dest) {
            Source = source;
            Dest = dest;
        }


        public string Source { get; }

        public string Dest { get; }

        /// <summary>Null until it collides and is answered for.</summary>
        public ConflictResolution? Choice { get; set; }
    }

    private sealed record GroupPlan(IReadOnlyList<MemberPlan> Members);

    private readonly record struct Outcome(BatchItemStatus Status, bool Applied, Exception? Failure);

    /// <summary>What one batch carries between its groups.</summary>
    private sealed class Run {
        public Run(bool isMove, IConflictResolver resolver, int itemCount) {
            IsMove = isMove;
            Resolver = resolver;
            ItemCount = itemCount;
        }


        public bool IsMove { get; }

        public IConflictResolver Resolver { get; }

        public int ItemCount { get; }

        public string Verb => IsMove ? "Move" : "Copy";

        /// <summary>Every answer the resolver gave, nested ones included, by source path.</summary>
        public Dictionary<string, ConflictResolution> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<IUndoableAction> UndoSteps { get; } = new();

        /// <summary>A late question was answered with Cancel: nothing more is applied.</summary>
        public bool Cancelled { get; set; }


        public ConflictResolution? AnswerFor(string source) {
            return Answers.TryGetValue(source, out var resolution) ? resolution : null;
        }
    }
}


// --- Batch result types (top-level so callers don't need to reach into BatchExecutor) ---

public sealed record BatchItemResult(string Source, string FinalDestination, BatchItemStatus Status, Exception? Error);
public enum BatchItemStatus { Ok, Skipped, Replaced, Renamed, Merged, Cancelled, Failed }

public sealed record DeleteResult(string Path, DeleteStatus Status, Exception? Error);
public enum DeleteStatus { Ok, Failed, Cancelled }
