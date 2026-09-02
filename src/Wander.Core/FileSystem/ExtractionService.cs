using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Shell;
using Wander.Core.Undo;

namespace Wander.Core.FileSystem;

/// <summary>
/// Taking things out of an archive onto the real filesystem. The same
/// obligations every file operation in Wander has - the system-path guard
/// on the target, a line in the log, a conflict dialog with the usual four
/// answers, progress that can be cancelled, and an undo step - carried out
/// against a source <see cref="IFileSystem"/> cannot read.
///
/// <para>
/// Not folded into <see cref="BatchExecutor"/>: that one copies filesystem
/// to filesystem, one item at a time, and every line of it assumes both
/// ends are paths it can stat. Here the bytes are moved by the shell in a
/// single call (<see cref="IShellNamespace.CopyOut"/>) - a solid 7z is
/// decompressed once instead of once per entry - and the class's work is
/// everything around that call. The shape repeats; the code does not.
/// </para>
///
/// <para>
/// Undo sends what was extracted to the recycle bin, and a Replace's
/// recycled original comes back with it: the composite unwinds in reverse,
/// so the copies go first and the file they overwrote is restored after.
/// </para>
/// </summary>
public sealed class ExtractionService {
    private readonly IShellNamespace _ns;
    private readonly IFileSystem _fs;
    private readonly IRecycleBin _bin;
    private readonly UndoService _undo;
    private readonly OperationTracker _tracker;
    private readonly ILogger _log;


    public ExtractionService(
        IShellNamespace ns, IFileSystem fs, IRecycleBin bin,
        UndoService undo, OperationTracker tracker, ILogger log) {
        _ns = ns;
        _fs = fs;
        _bin = bin;
        _undo = undo;
        _tracker = tracker;
        _log = log;
    }


    /// <summary>
    /// Extracts <paramref name="sources"/> (paths inside one or more
    /// archives) into <paramref name="targetFolder"/>.
    /// </summary>
    /// <returns>
    /// One result per source, in the order they were given - the same
    /// shape a copy or a move reports, so the status bar formats all three
    /// the same way.
    /// </returns>
    /// <exception cref="IOException">The target folder is a protected system path.</exception>
    public async Task<IReadOnlyList<BatchItemResult>> ExtractAsync(
        IReadOnlyList<string> sources, string targetFolder,
        IConflictResolver resolver, CancellationToken ct) {

        if (SystemPathGuard.IsProtected(targetFolder, out string guardReason)) {
            _log.Warn($"Extract refused: {targetFolder} ({guardReason})");

            throw new IOException(guardReason);
        }

        var plans = sources.Select(source => new Plan(source, Path.Combine(targetFolder, NameOf(source)))).ToList();
        var results = new BatchItemResult[plans.Count];
        var queue = new List<CopyOutItem>(plans.Count);
        var queued = new List<int>(plans.Count);
        var restores = new List<IUndoableAction>();

        if (!ResolveConflicts(plans, results, queue, queued, restores, resolver)) {
            // Cancelled before anything moved. The recycled originals of the
            // Replaces answered before the Cancel are still undoable.
            PushUndo(restores, Array.Empty<string>());

            return results;
        }

        if (queue.Count == 0) {
            PushUndo(restores, Array.Empty<string>());

            return results;
        }

        // Progress counts the things the user selected, not the files inside
        // the folders among them: they chose four entries, not four hundred.
        using var operation = _tracker.Begin("Extract", queue.Count);
        var landed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var progress = new InlineProgress(path => {
            lock (landed) {
                landed.Add(path);
            }
            operation.Advance(path);
        });

        Exception? failure = null;
        try {
            await _ns.CopyOut(queue, targetFolder, progress, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            failure = null;
        } catch (Exception ex) {
            _log.Error($"Extract failed into {targetFolder}", ex);
            failure = ex;
        }

        // The engine reports each item as it finishes, so what landed is
        // known even when the call as a whole threw or was cancelled. A
        // report can still be in flight on the dispatcher, so the set is
        // read under the same lock the reports take.
        HashSet<string> done;
        lock (landed) {
            done = new HashSet<string>(landed, StringComparer.OrdinalIgnoreCase);
        }

        var extracted = new List<string>(queue.Count);
        for (int i = 0; i < queued.Count; i++) {
            int index = queued[i];
            var plan = plans[index];
            if (done.Contains(plan.Source)) {
                results[index] = new BatchItemResult(plan.Source, plan.Destination, plan.Status, null);
                extracted.Add(plan.Destination);
                _log.Info($"Extract: {plan.Source} -> {plan.Destination} [{plan.Status}]");
            } else {
                results[index] = failure is null
                    ? new BatchItemResult(plan.Source, plan.Destination, BatchItemStatus.Cancelled, null)
                    : new BatchItemResult(plan.Source, plan.Destination, BatchItemStatus.Failed, failure);
            }
        }

        PushUndo(restores, extracted);

        return results;
    }

    /// <summary>
    /// One entry, copied out with no questions asked, for a target folder
    /// Wander owns and cleans up itself - the temporary copy behind
    /// "open". Deliberately outside the rules the method above follows:
    /// a scratch copy of somebody's file is not the user's data, so it
    /// carries no undo step and asks about no conflicts (the folder it goes
    /// into was made for it). Written down in ARCHITECTURE.md.
    /// </summary>
    /// <returns>The path of the copy.</returns>
    public async Task<string> ExtractToTempAsync(string source, string tempFolder, CancellationToken ct) {
        _fs.CreateDirectory(tempFolder);
        string destination = Path.Combine(tempFolder, NameOf(source));
        if (_fs.FileExists(destination)) {
            _fs.DeleteFile(destination);
        }

        await _ns.CopyOut(new[] { new CopyOutItem(source) }, tempFolder, null, ct).ConfigureAwait(false);
        _log.Info($"Extract (temporary copy): {source} -> {destination}");

        return destination;
    }


    /// <summary>
    /// Asks about every collision and fills in what each answer means:
    /// results for the ones that will not be copied, a queue entry for the
    /// ones that will, and a restore step for every target sent to the bin.
    /// </summary>
    /// <returns>False when the user cancelled the whole batch.</returns>
    private bool ResolveConflicts(
        IReadOnlyList<Plan> plans, BatchItemResult[] results,
        List<CopyOutItem> queue, List<int> queued, List<IUndoableAction> restores,
        IConflictResolver resolver) {

        int conflicts = plans.Count(p => Exists(p.Destination));
        ConflictResolution? batchAnswer = null;
        if (conflicts > 0) {
            batchAnswer = resolver.StartBatch(conflicts);
            if (batchAnswer == ConflictResolution.Cancel) {
                _log.Info($"Extract cancelled before start ({plans.Count} items, {conflicts} conflicts)");
                FillCancelled(plans, results, from: 0);

                return false;
            }
        }

        for (int i = 0; i < plans.Count; i++) {
            var plan = plans[i];
            var answer = Exists(plan.Destination)
                ? batchAnswer ?? resolver.Resolve(BuildInfo(plan))
                : (ConflictResolution?)null;

            switch (answer) {
                case ConflictResolution.Cancel:
                    FillCancelled(plans, results, from: i);

                    return false;
                case ConflictResolution.Skip:
                    results[i] = new BatchItemResult(plan.Source, plan.Destination, BatchItemStatus.Skipped, null);
                    continue;
                case ConflictResolution.Rename:
                    plan.Destination = UniqueName(plan.Destination);
                    plan.NewName = NameOf(plan.Destination);
                    plan.Status = BatchItemStatus.Renamed;
                    break;
                case ConflictResolution.Replace:
                    // "Everything undoable": the replaced file goes to the
                    // bin, never into oblivion, and the shell's copy engine
                    // then finds the way clear.
                    if (SystemPathGuard.IsProtected(plan.Destination, out string reason)) {
                        results[i] = new BatchItemResult(
                            plan.Source, plan.Destination, BatchItemStatus.Failed, new IOException(reason));
                        continue;
                    }
                    restores.Add(new DeleteAction(_bin, _bin.Send(plan.Destination)));
                    plan.Status = BatchItemStatus.Replaced;
                    break;
                case null:
                    break;
            }

            queue.Add(new CopyOutItem(plan.Source, plan.NewName));
            queued.Add(i);
        }

        return true;
    }

    private void PushUndo(IReadOnlyList<IUndoableAction> restores, IReadOnlyList<string> extracted) {
        var steps = new List<IUndoableAction>(restores.Count + extracted.Count);
        steps.AddRange(restores);
        foreach (string path in extracted) {
            steps.Add(new ExtractAction(_bin, path));
        }

        if (steps.Count == 0) {
            return;
        }

        _undo.Push(steps.Count == 1
            ? steps[0]
            : new CompositeAction(
                extracted.Count == 1 ? steps[^1].Description : $"extract of {extracted.Count} items",
                steps));
    }

    private static void FillCancelled(IReadOnlyList<Plan> plans, BatchItemResult[] results, int from) {
        for (int i = from; i < plans.Count; i++) {
            results[i] = new BatchItemResult(
                plans[i].Source, plans[i].Destination, BatchItemStatus.Cancelled, null);
        }
    }

    /// <summary>
    /// What the conflict dialog compares. The source's size and date come
    /// from the archive's own listing - one enumeration per parent folder,
    /// which is why the answers are looked up rather than asked for one at
    /// a time.
    /// </summary>
    private FileConflictInfo BuildInfo(Plan plan) {
        var source = _ns.Enumerate(Path.GetDirectoryName(plan.Source) ?? "")
            .FirstOrDefault(e => string.Equals(e.FullPath, plan.Source, StringComparison.OrdinalIgnoreCase));

        return new FileConflictInfo(source ?? Unknown(plan.Source), _fs.GetEntry(plan.Destination) ?? Unknown(plan.Destination));
    }

    private bool Exists(string path) => _fs.FileExists(path) || _fs.DirectoryExists(path);

    private string UniqueName(string desired) {
        string dir = Path.GetDirectoryName(desired) ?? "";
        string stem = Path.GetFileNameWithoutExtension(desired);
        string extension = Path.GetExtension(desired);
        int i = 1;
        while (true) {
            string candidate = Path.Combine(dir, $"{stem} ({i}){extension}");
            if (!Exists(candidate)) {
                return candidate;
            }
            i++;
        }
    }

    private static string NameOf(string path) {
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static FileSystemEntry Unknown(string path) {
        return new FileSystemEntry(
            Name: NameOf(path),
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
    /// Reports on the thread that calls it, unlike
    /// <see cref="Progress{T}"/>, which posts to a synchronization context
    /// and so can still be in flight when the copy has returned. What
    /// landed has to be known by then: it decides what is reported as done
    /// and what goes into the undo step.
    /// </summary>
    private sealed class InlineProgress : IProgress<string> {
        private readonly Action<string> _report;

        public InlineProgress(Action<string> report) {
            _report = report;
        }

        public void Report(string value) => _report(value);
    }


    /// <summary>One source, and what has been decided about it so far.</summary>
    private sealed class Plan {
        public Plan(string source, string destination) {
            Source = source;
            Destination = destination;
        }

        public string Source { get; }
        public string Destination { get; set; }

        /// <summary>Non-null only after a Rename answer - the engine renames on the fly.</summary>
        public string? NewName { get; set; }

        public BatchItemStatus Status { get; set; } = BatchItemStatus.Ok;
    }
}
