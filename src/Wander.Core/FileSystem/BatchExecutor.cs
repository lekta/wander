using System.Globalization;
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
    private readonly HeldPaths _held;
    private readonly RetryPolicy _retry;


    /// <param name="held">Claims and the wait for held paths; null has no probe and nobody to name a holder.</param>
    /// <param name="retry">How a transient error is tried again (<see cref="IoRetry"/>); <see cref="RetryPolicy.Default"/> when null.</param>
    public BatchExecutor(
        IFileSystem fs, IRecycleBin bin, UndoService undo, OperationTracker tracker, ILogger log, HeldPaths? held = null,
        RetryPolicy? retry = null) {
        _fs = fs;
        _bin = bin;
        _undo = undo;
        _tracker = tracker;
        _log = log;
        _held = held ?? new HeldPaths(new PathClaims());
        _retry = retry ?? RetryPolicy.Default;
    }


    /// <summary>
    /// How a file is copied, by its source: read back or not
    /// ("Проверять скопированное"), Windows' engine only or not. The
    /// service sets it from the settings; plain copies until then.
    /// </summary>
    public Func<string, CopyOptions> CopyOptionsFor { get; set; } = _ => CopyOptions.Plain;


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
        string verb = permanent ? OperationVerbs.DeletePermanently : OperationVerbs.Recycle;
        using var op = _tracker.Begin(verb, paths.Count, token: ct);
        using var claim = _held.Claims.Claim(paths, ClaimKind.UserOperation, verb);
        var results = await Task.Run(
            () => DeleteManyCore(paths, permanent, op, claim, ct),
            ct).ConfigureAwait(false);
        op.Finish(results.Select(ItemOf).ToList(), target: null);

        return results;
    }


    // --- Internals -----------------------------------------------------

    private async Task<IReadOnlyList<BatchItemResult>> RunBatchAsync(
        IReadOnlyList<BatchGroup> groups, string targetFolder, bool isMove, IConflictResolver resolver,
        CancellationToken ct) {
        // Progress counts groups, not files: the user dragged three photos,
        // not three photos and three sidecars. Bytes count everything,
        // sidecars included - the disk does not care who asked for them.
        string verb = isMove ? OperationVerbs.Move : OperationVerbs.Copy;
        using var op = _tracker.Begin(verb, groups.Count, token: ct);
        // The debug switch, first thing in the operation's lines: a copy
        // complained about is told apart by its engine.
        if (groups.Count > 0 && CopyOptionsFor(groups[0].Primary).SystemOnly) {
            _log.Warn($"{(isMove ? "Move" : "Copy")}: Windows' copy engine only (debug switch)");
        }

        // The sources and where they go, claimed for as long as the batch
        // runs: an operation that reaches for them meanwhile names this one
        // instead of fighting it.
        var claimed = groups
            .SelectMany(g => g.All)
            .SelectMany(source => new[] { source, Path.Combine(targetFolder, NameOf(source)) });
        using var claim = _held.Claims.Claim(claimed, ClaimKind.UserOperation, verb);

        var results = await Task.Run(
            () => ApplyBatch(groups, targetFolder, isMove, resolver, op, ct, weigh: true, own: claim),
            ct).ConfigureAwait(false);
        // One item per result, that is per group: the companions are folded
        // into their main file here as everywhere else.
        if (op.Finish(results.Select(ItemOf).ToList(), targetFolder) is { } record) {
            _log.Info(Summary(isMove ? "Move" : "Copy", record));
        }

        return results;
    }

    /// <summary>
    /// "Copy done: 12 items, 12.3 GB, 327.4 s, 38.5 MB/s; failed 1, skipped 0" -
    /// the operation in one line of the technical log, beside the line per item.
    /// </summary>
    internal static string Summary(string verb, OperationRecord record) {
        double seconds = record.Elapsed.TotalSeconds;
        double mbPerSecond = seconds > 0 ? record.Bytes / 1048576.0 / seconds : 0;
        string size = record.Bytes >= 1L << 30
            ? (record.Bytes / (double)(1L << 30)).ToString("F1", CultureInfo.InvariantCulture) + " GB"
            : (record.Bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";

        return string.Create(CultureInfo.InvariantCulture,
            $"{verb} done: {record.Total} items, {size}, {seconds:F1} s, {mbPerSecond:F1} MB/s; failed {record.Failed}, skipped {record.Skipped}, cancelled {record.Cancelled}, verified {record.Verified}");
    }

    private static OperationItem ItemOf(BatchItemResult result) {
        return new OperationItem(result.Source, result.FinalDestination, result.Status, result.Error, result.Attempts, result.Verified, result.Engine);
    }

    private static OperationItem ItemOf(DeleteResult result) {
        var status = result.Status switch {
            DeleteStatus.Ok => BatchItemStatus.Ok,
            DeleteStatus.Cancelled => BatchItemStatus.Cancelled,
            _ => BatchItemStatus.Failed,
        };

        return new OperationItem(result.Path, null, status, result.Error);
    }

    /// <summary>
    /// How many bytes the batch is about to move, counted before anything
    /// moves - and <b>after</b> the question about collisions
    /// (<see cref="AskUpFront"/>): the walk of a large tree takes seconds,
    /// and with the walk first the operation stood there empty for all of
    /// them before a question that a Cancel makes the walk pointless for
    /// (2026-09-21). A file is its own size, a folder is the walk of it. Zero
    /// when nothing could be measured, and the display falls back to counting
    /// items.
    ///
    /// <para>
    /// The walk carries the same depth guard as every other walk in Wander
    /// (<see cref="FolderStatistics.DefaultMaxDepth"/>), and an unreadable
    /// subtree is skipped rather than fatal: an estimate that is a little
    /// low costs a bar that finishes early, and a batch that refuses to
    /// start because one folder is locked costs the whole operation.
    /// </para>
    /// </summary>
    private long Weigh(IReadOnlyList<BatchGroup> groups, Dictionary<string, long> into, CancellationToken ct) {
        long total = 0;
        foreach (string source in groups.SelectMany(g => g.All)) {
            if (ct.IsCancellationRequested) {
                into.Clear();

                return 0;
            }

            try {
                long size = _fs.DirectoryExists(source)
                    ? FolderStatistics.Collect(_fs, source, maxTypes: 0, ct: ct).TotalSize
                    : _fs.GetEntry(source)?.Size ?? 0;
                into[source] = size;
                total += size;
            } catch (OperationCanceledException) {
                into.Clear();

                return 0;
            } catch (Exception ex) {
                _log.Info($"Cannot size {source} for the progress bar - {ex.Message}");
            }
        }

        return total;
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
        IOperationHandle? progress, CancellationToken ct = default, bool weigh = false, IDisposable? own = null) {
        using var _ = _undo.BeginOperation();
        using var gate = _held.Begin(_log, own);

        var plans = groups.Select(g => Plan(g, targetFolder)).ToList();
        var run = new Run(isMove, resolver, plans.Count, gate) {
            Progress = progress,
            Token = ct,
        };

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

        if (weigh && progress is not null) {
            var weights = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            progress.SetWeighing(true);
            try {
                progress.SetTotalBytes(Weigh(groups, weights, ct));
            } finally {
                progress.SetWeighing(false);
            }
            run.Weights = weights;
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

            progress?.SetCurrentPath(plan.Members[0].Source);
            var (result, applied) = ApplyGroup(plan, run);
            results.Add(result);
            // A link left out of a copied folder is an item of its own in
            // the record: skipped, with why.
            foreach (string link in run.GroupCopy.SkippedLinks ?? Array.Empty<string>()) {
                _log.Info($"{run.Verb}: {link} - a link to a folder, not copied");
                results.Add(new BatchItemResult(link, "", BatchItemStatus.Skipped, new LinkNotCopiedException(link)));
            }
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
    /// The group's status is the main file's; Failed when any member failed.
    /// A copy leaves the members that made it - the composite undo covers
    /// them, and the user has to be told rather than shown "mostly fine". A
    /// move puts back what of the group had moved (I2): a photo on one side
    /// and its sidecar on the other is the one outcome worse than either.
    /// </para>
    /// </summary>
    private (BatchItemResult Result, bool Applied) ApplyGroup(GroupPlan plan, Run run) {
        var primary = plan.Members[0];
        string[]? renamed = null;
        Exception? failure = null;
        bool anyApplied = false;
        var primaryStatus = BatchItemStatus.Ok;
        string primaryDest = primary.Dest;
        int stepsBefore = run.UndoSteps.Count;
        run.GroupBusy = null;
        run.GroupAttempts = 1;
        run.GroupCopy = CopyReport.None;

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

        if (failure is not null && run.IsMove && anyApplied && plan.Members.Count > 1) {
            anyApplied = !PutBack(run, stepsBefore, primary.Source);
        }

        return failure is null
            ? (new BatchItemResult(primary.Source, primaryDest, primaryStatus, null, run.GroupBusy, run.GroupAttempts,
                run.GroupCopy.Verified, run.GroupCopy.Engine), anyApplied)
            : (new BatchItemResult(primary.Source, primaryDest, BatchItemStatus.Failed, failure, run.GroupBusy, run.GroupAttempts,
                false, run.GroupCopy.Engine), anyApplied);
    }

    /// <summary>
    /// A move group in which a member failed: what of it had moved goes back,
    /// last first - the undo steps it left are run and taken off the batch's
    /// composite, as <c>RenameMany</c> rolls back. A step that will not go
    /// back stays in the composite with the ones before it, so <c>Ctrl+Z</c>
    /// can still try.
    /// </summary>
    /// <returns>True when everything went back.</returns>
    private bool PutBack(Run run, int stepsBefore, string primary) {
        for (int i = run.UndoSteps.Count - 1; i >= stepsBefore; i--) {
            try {
                run.UndoSteps[i].Undo();
                run.UndoSteps.RemoveAt(i);
            } catch (Exception ex) {
                _log.Error($"{run.Verb}: the group of {primary} failed, and putting it back failed too", ex);

                return false;
            }
        }
        _log.Info($"{run.Verb}: the group of {primary} failed - what of it had moved is back");

        return true;
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

        // The copy itself began: what is at the destination from here on is
        // this item's, not a folder that was there before.
        bool started = false;
        try {
            // System-path guard: moving a protected path away is as
            // destructive as deleting it; replacing a protected target
            // would recycle a system file.
            if (run.IsMove && SystemPathGuard.IsProtected(src, out string srcReason)) {
                throw new IOException(srcReason);
            }
            // A move takes the source away: another operation of the user's
            // on it is named, a reader of ours is told to let go.
            if (run.IsMove) {
                run.Gate.Check(src);
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
                if (RecycleUndo.Of(_bin.Send(dest), _bin, _undo, _log) is { } restore) {
                    run.UndoSteps.Add(restore);
                }
            }

            started = true;
            var copied = ApplyOne(src, dest, run);
            run.UndoSteps.Add(run.IsMove
                ? new MoveAction(_fs, src, dest)
                : new CreateAction(_bin, dest, OperationVerbs.Copy));
            var status = choice switch {
                ConflictResolution.Replace => BatchItemStatus.Replaced,
                ConflictResolution.Rename => BatchItemStatus.Renamed,
                _ => BatchItemStatus.Ok,
            };
            _log.Info($"{run.Verb}: {src} -> {dest} [{status}{Engine(copied)}]");

            return new Outcome(status, true, null);
        } catch (OperationCanceledException) {
            // Cancel now reaches inside an item, not only between items: a
            // copy stops in the middle of a file. That is a cancellation,
            // not a failure, and it must not be reported as one.
            //
            // A file is copied under a temporary name and renamed when whole
            // (TransientFiles.TempNameFor), so a cancelled one leaves nothing
            // at its name to undo. A folder can be part-copied, and what did
            // land is undoable like everything else that lands - "Отмена"
            // then "Ctrl+Z" leaves no leftovers. For a move the source is
            // still where it was, so binning the partial destination is the
            // right undo there too.
            run.Cancelled = true;
            if (_fs.DirectoryExists(dest)) {
                run.UndoSteps.Add(new CreateAction(_bin, dest, run.IsMove ? OperationVerbs.Move : OperationVerbs.Copy));
                _log.Info($"{run.Verb} cancelled part-way: {src} -> {dest} (partial copy is undoable)");

                return new Outcome(BatchItemStatus.Cancelled, true, null);
            }
            _log.Info($"{run.Verb} cancelled: {src} -> {dest}");

            return new Outcome(BatchItemStatus.Cancelled, false, null);
        } catch (Exception ex) {
            _log.Error($"{run.Verb} failed: {src} -> {dest}", ex);
            // A folder copy that failed part-way - a bad card, a mismatch on
            // the check - left what it had copied, as a cancel does, and it
            // is undone the same way (review of sprint 2). A move takes its
            // partial copy back itself (IFileSystem.MoveEntry).
            if (started && !run.IsMove && _fs.DirectoryExists(dest)) {
                run.UndoSteps.Add(new CreateAction(_bin, dest, OperationVerbs.Copy));
                _log.Info($"{run.Verb} failed part-way: {src} -> {dest} (partial copy is undoable)");

                return new Outcome(BatchItemStatus.Failed, true, ex);
            }

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
            if (RecycleUndo.Of(_bin.Send(src), _bin, _undo, _log) is { } restore) {
                run.UndoSteps.Add(restore);
            }
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

    /// <summary>", own, verified" - what the log line of an item says about how its bytes went.</summary>
    private static string Engine(CopyReport copied) {
        string engine = copied.Engine switch {
            CopyEngine.Own => ", own",
            CopyEngine.System => ", windows",
            _ => "",
        };

        return copied.Verified ? engine + ", verified" : engine;
    }

    private static BatchItemResult Cancelled(GroupPlan plan) {
        return new BatchItemResult(plan.Members[0].Source, plan.Members[0].Dest, BatchItemStatus.Cancelled, null);
    }

    private static string NameOf(string path) {
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static bool SameRoot(string a, string b) {
        return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<DeleteResult> DeleteManyCore(
        IReadOnlyList<string> paths, bool permanent, IOperationHandle progress, IDisposable own, CancellationToken ct) {
        using var _ = _undo.BeginOperation();
        using var gate = _held.Begin(_log, own);

        var undoSteps = new List<IUndoableAction>(paths.Count);
        var results = permanent
            ? DeleteForGood(paths, progress, gate, ct)
            : Recycle(paths, progress, undoSteps, gate, ct);

        if (permanent) {
            // Not undoable, and neither is anything whose undo needs what is
            // gone; the rest of the history stays.
            _undo.Forget(results.Where(r => r.Status == DeleteStatus.Ok).Select(r => r.Path).ToList());
            // A folder that failed half-way is partly gone: what went is
            // forgotten as well, what is still there stays undoable.
            var partly = results.Where(r => r.Status != DeleteStatus.Ok && _fs.DirectoryExists(r.Path)).Select(r => r.Path).ToList();
            if (partly.Count > 0) {
                _undo.ForgetLost(partly, p => _fs.FileExists(p) || _fs.DirectoryExists(p));
            }
        } else {
            PushComposite(undoSteps, isMove: false, undoSteps.Count, verbOverride: "delete", undoVerb: OperationVerbs.Recycle);
            // What the bin took without a way back is as gone as a permanent
            // delete, and so is what older undo steps would need of it.
            var lost = results.Where(r => r.Permanent).Select(r => r.Path).ToList();
            if (lost.Count > 0) {
                _undo.Forget(lost);
            }
        }

        return results;
    }

    private List<DeleteResult> DeleteForGood(IReadOnlyList<string> paths, IOperationHandle progress, BusyGate gate, CancellationToken ct) {
        var results = new List<DeleteResult>(paths.Count);
        foreach (string path in paths) {
            if (ct.IsCancellationRequested) {
                results.Add(new DeleteResult(path, DeleteStatus.Cancelled, null));
                continue;
            }

            try {
                if (SystemPathGuard.IsProtected(path, out string guardReason)) {
                    throw new IOException(guardReason);
                }
                gate.Check(path);
                // A held file is waited for, as a move waits: a file is asked
                // before the delete, a folder is deleted again while it
                // answers "in use" - out of the operation's one budget.
                BusyReport? busy;
                if (_fs.DirectoryExists(path)) {
                    busy = gate.Retry(path, () => _fs.DeleteDirectory(path, recursive: true), ct);
                } else if (_fs.FileExists(path)) {
                    busy = gate.WaitForFile(path, ct);
                    _fs.DeleteFile(path);
                } else {
                    throw new FileNotFoundException("Path not found", path);
                }
                _log.Warn($"Permanent delete: {path}");
                results.Add(new DeleteResult(path, DeleteStatus.Ok, null, busy));
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                // Stopped while waiting for it: still there, as asked.
                results.Add(new DeleteResult(path, DeleteStatus.Cancelled, null));
            } catch (Exception ex) {
                _log.Error($"Delete failed: {path}", ex);
                results.Add(new DeleteResult(path, DeleteStatus.Failed, ex));
            }

            progress.Advance(path);
        }

        return results;
    }

    /// <summary>
    /// The guard and the claims first, item by item; what passes goes to the
    /// bin as one batch (<see cref="IRecycleBin.SendMany"/>) and comes back
    /// with a result each, put together again in the order asked.
    ///
    /// <para>
    /// What the bin finds held comes back "in use" in milliseconds, with the
    /// rest of the batch done, and is sent again - all of it together - every
    /// step of the wait while the operation's budget lasts
    /// (<see cref="BusyGate.RetryMany"/>). Progress counts an item once, on
    /// its first answer.
    /// </para>
    /// </summary>
    private List<DeleteResult> Recycle(
        IReadOnlyList<string> paths, IOperationHandle progress, List<IUndoableAction> undoSteps, BusyGate gate,
        CancellationToken ct) {
        var results = new DeleteResult?[paths.Count];
        var allowed = new List<int>(paths.Count);
        for (int i = 0; i < paths.Count; i++) {
            try {
                if (SystemPathGuard.IsProtected(paths[i], out string guardReason)) {
                    throw new IOException(guardReason);
                }
                gate.Check(paths[i]);
                allowed.Add(i);
            } catch (IOException refusal) {
                _log.Error($"Delete failed: {paths[i]}", refusal);
                results[i] = new DeleteResult(paths[i], DeleteStatus.Failed, refusal);
                progress.Advance(paths[i]);
            }
        }

        var targets = allowed.Select(i => paths[i]).ToList();
        var sent = new RecycleResult[targets.Count];
        Action<string>? advance = progress.Advance;
        var busy = gate.RetryMany(targets, indices => {
            // The long-path walk of a folder is made once: what is sent again
            // was held, not too long.
            var answers = _bin.SendMany(indices.Select(n => targets[n]).ToList(), advance, ct, checkedBefore: advance is null);
            advance = null;
            var held = new List<int>();
            for (int k = 0; k < indices.Count; k++) {
                sent[indices[k]] = answers[k];
                if (FileInUse.Is(answers[k].Error)) {
                    held.Add(indices[k]);
                }
            }

            return held;
        }, ct);

        for (int n = 0; n < sent.Length; n++) {
            string path = sent[n].Path;
            var report = busy.GetValueOrDefault(n);
            // Still held when the user stopped the wait: that is the cancel
            // it was, not a failure to report.
            bool givenUp = ct.IsCancellationRequested && report is { Released: false };
            if (sent[n].Handle is { IsUntraceable: true }) {
                // Gone, and nothing to find it by in the bin: no undo step that
                // could only fail - the result says it is gone for good.
                _log.Warn($"Delete (recycle): {path} - nothing to find it by in the bin, counted as deleted for good");
                results[allowed[n]] = new DeleteResult(path, DeleteStatus.Ok, null, report, Permanent: true);
            } else if (sent[n].Handle is { } handle) {
                undoSteps.Add(new DeleteAction(_bin, handle));
                _log.Info($"Delete (recycle): {path}");
                results[allowed[n]] = new DeleteResult(path, DeleteStatus.Ok, null, report);
            } else if (sent[n].Error is { } error && !givenUp) {
                _log.Error($"Delete failed: {path}", error);
                results[allowed[n]] = new DeleteResult(path, DeleteStatus.Failed, error, report);
            } else {
                results[allowed[n]] = new DeleteResult(path, DeleteStatus.Cancelled, null);
            }
        }

        return results.Select(r => r!).ToList();
    }

    private void PushComposite(
        IReadOnlyList<IUndoableAction> steps, bool isMove, int itemCount, string? verbOverride = null, string? undoVerb = null) {
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
        _undo.Push(steps.Count == 1
            ? steps[0]
            : new CompositeAction(desc, steps, undoVerb ?? (isMove ? OperationVerbs.Move : OperationVerbs.Copy), Math.Max(1, itemCount)));
    }

    /// <summary>
    /// The one call that moves bytes, and so the one that reports them. What
    /// the copy reports and what the estimate said are two different numbers
    /// - the estimate is a walk taken before the fact - so the difference is
    /// settled here, per entry: the counter ends up exactly where the plan
    /// said it would, however wrong either half was.
    /// </summary>
    /// <returns>What the copy went through - the engine, whether it was read back; nothing for a rename.</returns>
    private CopyReport ApplyOne(string src, string dest, Run run) {
        long expected = run.Progress is null ? 0 : ExpectedBytes(run, src);
        long reported = 0;
        var options = CopyOptionsFor(src);
        var report = CopyReport.None;
        var bytes = run.Progress is null
            ? null
            : new InlineProgress(delta => {
                reported += delta;
                run.Progress.AdvanceBytes(delta);
            });

        try {
            // A Replace conflict never reaches here with the target still in
            // place - ApplyEntry recycles it first - so plain no-overwrite
            // semantics are enough for both branches.
            //
            // A held source is waited for: a file
            // is asked before the move, a folder has nothing to ask and the
            // move itself is tried again while it answers "in use".
            //
            // The wait is made for every entry, whatever the group already
            // has to report: "x ??= Wait()" does not call Wait() once x is
            // set, and for a folder the call is the move itself.
            //
            // Not across volumes: there a folder move is a copy and then a
            // delete, and a delete that meets a held file has already taken
            // everything else. Trying the move again copies onto the copy,
            // and a Cancel during the wait took the finished copy for a
            // partial one - Ctrl+Z then binned the only whole copy there was.
            if (run.IsMove && _fs.DirectoryExists(src) && SameRoot(src, dest)) {
                var held = run.Gate.Retry(src, () => _fs.MoveEntry(src, dest, bytes, run.Token), run.Token);
                run.GroupBusy ??= held;
            } else if (run.IsMove && _fs.DirectoryExists(src)) {
                report = _fs.MoveEntry(src, dest, bytes, run.Token, options);
            } else if (run.IsMove) {
                var held = run.Gate.WaitForFile(src, run.Token);
                run.GroupBusy ??= held;
                if (SameRoot(src, dest)) {
                    _fs.MoveEntry(src, dest, bytes, run.Token);
                } else {
                    Retrying(() => report = _fs.MoveEntry(src, dest, bytes, run.Token, options));
                }
            } else if (_fs.DirectoryExists(src)) {
                report = _fs.CopyDirectory(src, dest, overwrite: false, bytes, run.Token, options);
            } else {
                Retrying(() => report = _fs.CopyFile(src, dest, overwrite: false, bytes, run.Token, options));
            }
            run.GroupCopy = run.GroupCopy.And(report);

            return report;
        } finally {
            // Only where the plan had a number to settle against. Nothing
            // walked the insides of a merged folder before the fact, so its
            // entries are counted by what the copy actually reported, and
            // subtracting a zero "expectation" would cancel that out.
            if (expected > 0) {
                run.Progress?.AdvanceBytes(expected - reported);
            }
        }

        // A file copied - or moved to another volume, which is a copy - is
        // tried again while it fails the way a card or a cable fails for a
        // moment (TransientIo). The try that failed left nothing behind (the
        // copy goes under a temporary name), but it did count its bytes:
        // those are taken back, and the next try counts them again. A folder
        // is not tried again: half of it is already there.
        void Retrying(Action io) {
            int tries = 1;
            try {
                IoRetry.Run(io, _retry, run.Token, (attempt, error) => {
                    tries = attempt;
                    _log.Warn($"{run.Verb}: {src} -> {dest} - {error.Message} (0x{error.HResult:X8}); try {attempt} of {_retry.Attempts}");
                    run.Progress?.AdvanceBytes(-reported);
                    reported = 0;
                    run.Progress?.SetAttempt(attempt);
                });
            } finally {
                run.GroupAttempts = Math.Max(run.GroupAttempts, tries);
                if (tries > 1) {
                    run.Progress?.SetAttempt(0);
                }
            }
        }
    }

    /// <summary>
    /// What the up-front walk said this source weighs. A file it never saw -
    /// an entry inside a merged folder - is worth one cheap stat; a folder
    /// it never saw is left at zero rather than walked in the middle of the
    /// operation.
    /// </summary>
    private long ExpectedBytes(Run run, string source) {
        if (run.Weights is not null && run.Weights.TryGetValue(source, out long known)) {
            return known;
        }

        try {
            return _fs.DirectoryExists(source) ? 0 : _fs.GetEntry(source)?.Size ?? 0;
        } catch {
            return 0;
        }
    }

    private bool Exists(string path) => _fs.FileExists(path) || _fs.DirectoryExists(path);

    /// <summary>The number after the highest already there - the one rule, see <see cref="UniqueNames"/>.</summary>
    private string GenerateUniqueName(string desiredPath) {
        return Wander.Core.FileSystem.UniqueNames.Resolve(desiredPath, Exists, folder => _fs.Enumerate(folder).Select(e => e.Name));
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


    /// <summary>
    /// Reports on the thread that calls it, unlike <see cref="Progress{T}"/>,
    /// which posts to a synchronization context: these deltas are counted,
    /// not displayed, and one still in flight when the copy returns would be
    /// counted twice by the true-up.
    /// </summary>
    private sealed class InlineProgress : IProgress<long> {
        private readonly Action<long> _report;

        public InlineProgress(Action<long> report) {
            _report = report;
        }

        public void Report(long value) => _report(value);
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
        public Run(bool isMove, IConflictResolver resolver, int itemCount, BusyGate gate) {
            IsMove = isMove;
            Resolver = resolver;
            ItemCount = itemCount;
            Gate = gate;
        }


        public bool IsMove { get; }

        public IConflictResolver Resolver { get; }

        public int ItemCount { get; }

        /// <summary>Claims and the wait for held paths, for the whole batch.</summary>
        public BusyGate Gate { get; }

        /// <summary>The first held path the current group waited for; reset at each group.</summary>
        public BusyReport? GroupBusy { get; set; }

        /// <summary>What the current group's copies went through, all members together; reset at each group.</summary>
        public CopyReport GroupCopy { get; set; } = CopyReport.None;

        /// <summary>The most tries any member of the current group took; reset at each group.</summary>
        public int GroupAttempts { get; set; } = 1;

        public string Verb => IsMove ? "Move" : "Copy";

        /// <summary>Every answer the resolver gave, nested ones included, by source path.</summary>
        public Dictionary<string, ConflictResolution> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<IUndoableAction> UndoSteps { get; } = new();

        /// <summary>A late question was answered with Cancel: nothing more is applied.</summary>
        public bool Cancelled { get; set; }

        /// <summary>Where bytes are reported; null for the sync entry points.</summary>
        public IOperationHandle? Progress { get; init; }

        /// <summary>Cancels a copy in the middle of a file, not only between files.</summary>
        public CancellationToken Token { get; init; }

        /// <summary>What the up-front walk weighed, by source path; null for the sync entry points.</summary>
        public IReadOnlyDictionary<string, long>? Weights { get; set; }


        public ConflictResolution? AnswerFor(string source) {
            return Answers.TryGetValue(source, out var resolution) ? resolution : null;
        }
    }
}


// --- Batch result types (top-level so callers don't need to reach into BatchExecutor) ---

/// <param name="Busy">A source of the item was held and waited for - worth a line in the journal either way.</param>
/// <param name="Attempts">How many tries it took: more than one after a transient error (<see cref="IoRetry"/>).</param>
/// <param name="Verified">Every copy in it was read back and matched (<see cref="CopyOptions.Verify"/>).</param>
/// <param name="Engine">What moved its bytes; <see cref="CopyEngine.None"/> for a rename.</param>
public sealed record BatchItemResult(
    string Source, string FinalDestination, BatchItemStatus Status, Exception? Error, BusyReport? Busy = null, int Attempts = 1,
    bool Verified = false, CopyEngine Engine = CopyEngine.None);

/// <param name="Busy">The item was held and waited for - worth a line in the journal either way.</param>
/// <param name="Permanent">
/// Gone with no way back although it was sent to the bin: the bin took it
/// and named nothing to find it by (<see cref="RecycleHandle.IsUntraceable"/>).
/// </param>
public sealed record DeleteResult(string Path, DeleteStatus Status, Exception? Error, BusyReport? Busy = null, bool Permanent = false);
public enum DeleteStatus { Ok, Failed, Cancelled }
