using System.Globalization;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Undo;

namespace Wander.Core.Shell;

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
    private readonly PathClaims _claims;


    /// <param name="claims">Where an extraction claims what it reads and writes; null keeps them to itself.</param>
    public ExtractionService(
        IShellNamespace ns, IFileSystem fs, IRecycleBin bin,
        UndoService undo, OperationTracker tracker, ILogger log, PathClaims? claims = null) {
        _ns = ns;
        _fs = fs;
        _bin = bin;
        _undo = undo;
        _tracker = tracker;
        _log = log;
        _claims = claims ?? new PathClaims();
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
    /// <exception cref="IOException">The target folder is in the Windows tree (<see cref="SystemPathGuard.MayWriteInto"/>).</exception>
    public async Task<IReadOnlyList<BatchItemResult>> ExtractAsync(
        IReadOnlyList<string> sources, string targetFolder,
        IConflictResolver resolver, CancellationToken ct) {

        // Adding to the folder, not taking it away: a drive root or the
        // profile takes an extraction, the Windows tree does not.
        if (!SystemPathGuard.MayWriteInto(targetFolder, out string guardReason)) {
            _log.Warn($"Extract refused: {targetFolder} ({guardReason})");

            throw new IOException(guardReason);
        }

        var plans = sources.Select(source => new Plan(source, Path.Combine(targetFolder, NameOf(source)))).ToList();
        // The archive entries and where they land, for as long as the
        // extraction runs: a delete of the archive meanwhile names it.
        using var claim = _claims.Claim(
            plans.SelectMany(p => new[] { p.Source, p.Destination }), ClaimKind.UserOperation, OperationVerbs.Extract);
        var results = new BatchItemResult[plans.Count];
        var queue = new List<CopyOutItem>(plans.Count);
        var queued = new List<int>(plans.Count);
        var restores = new List<IUndoableAction>();

        if (!await ResolveConflicts(plans, results, queue, queued, restores, resolver, ct).ConfigureAwait(false)) {
            // Cancelled before anything moved. The recycled originals of the
            // Replaces answered before the Cancel are still undoable.
            PushUndo(restores, Array.Empty<string>());

            return results;
        }

        if (queue.Count == 0) {
            PushUndo(restores, Array.Empty<string>());

            return results;
        }

        // The engine reports every file it lands, those inside a folder
        // too. A zip's directory says what each one weighs, so there the
        // operation counts files and bytes, and has a speed and a time left.
        // Anything else counts the entries the user selected, the inner files
        // only naming what is in hand, and the bar runs on the engine's own
        // "work" units: counting the inner files against the selected ones
        // stood the bar at 100% from the first file (2026-10-29).
        var weight = await Task.Run(() => Weigh(queue, ct), ct).ConfigureAwait(false);
        using var operation = weight is null
            ? _tracker.Begin(OperationVerbs.Extract, queue.Count, totalBytes: 0, bytesAreWork: true, token: ct)
            : _tracker.Begin(OperationVerbs.Extract, weight.Files, weight.Bytes, token: ct);
        var selected = new HashSet<string>(queue.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
        var landed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var progress = new InlineProgress(path => {
            lock (landed) {
                landed.Add(path);
            }
            if (weight is null) {
                if (selected.Contains(path)) {
                    operation.Advance(path);
                } else {
                    operation.SetCurrentPath(path);
                }
            } else if (weight.SizeOf(path) is { } size) {
                operation.Advance(path);
                operation.AdvanceBytes(size);
            } else {
                operation.SetCurrentPath(path);
            }
        });

        long reportedWork = 0;
        var work = weight is not null ? null : new InlineWorkProgress(units => {
            operation.SetTotalBytes(units.Total);
            operation.AdvanceBytes(units.Done - reportedWork);
            reportedWork = units.Done;
        });

        Exception? failure = null;
        try {
            await _ns.CopyOut(queue, targetFolder, progress, ct, work).ConfigureAwait(false);
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
    /// One entry, copied out with no questions asked, into scratch space
    /// Wander owns - the temporary copy behind "open". The work is
    /// <see cref="TempExtraction"/>'s, shared with the preview pane and the
    /// conflict window; this is the door onto it for a caller that already
    /// has the service in hand.
    /// </summary>
    /// <returns>The path of the copy.</returns>
    public Task<string> ExtractToTempAsync(string source, string tempFolder, CancellationToken ct) {
        return TempExtraction.CopyOutAsync(_ns, _fs, _log, source, tempFolder, ct);
    }


    /// <summary>
    /// Asks about every collision and fills in what each answer means:
    /// results for the ones that will not be copied, a queue entry for the
    /// ones that will, and a restore step for every target sent to the bin.
    /// </summary>
    /// <returns>False when the user cancelled the whole batch.</returns>
    private async Task<bool> ResolveConflicts(
        IReadOnlyList<Plan> plans, BatchItemResult[] results,
        List<CopyOutItem> queue, List<int> queued, List<IUndoableAction> restores,
        IConflictResolver resolver, CancellationToken ct) {

        var colliding = new List<int>();
        var infos = new List<FileConflictInfo>();
        var listings = new Dictionary<string, IReadOnlyList<FileSystemEntry>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < plans.Count; i++) {
            if (Exists(plans[i].Destination)) {
                colliding.Add(i);
                infos.Add(BuildInfo(plans[i], listings));
            }
        }

        await UnpackForComparison(infos, ct).ConfigureAwait(false);

        // Asked once, about the whole list, before the engine starts - see
        // IConflictResolver. Nothing has been copied yet, so a Cancel here
        // is a Cancel of everything.
        var answers = new ConflictResolution?[plans.Count];
        if (infos.Count > 0) {
            var replies = resolver.ResolveAll(new ConflictRequest(infos, plans.Count));
            if (replies is null || replies.Any(r => r.Resolution == ConflictResolution.Cancel)) {
                _log.Info($"Extract cancelled before start ({plans.Count} items, {infos.Count} conflicts)");
                FillCancelled(plans, results, from: 0);

                return false;
            }
            var byPath = replies.ToDictionary(r => r.Conflict.Source.FullPath, r => r.Resolution, StringComparer.OrdinalIgnoreCase);
            foreach (int i in colliding) {
                answers[i] = byPath.TryGetValue(plans[i].Source, out var resolution)
                    ? resolution
                    : throw new InvalidOperationException($"The conflict resolver left {plans[i].Source} unanswered.");
            }
        }

        for (int i = 0; i < plans.Count; i++) {
            var plan = plans[i];
            switch (answers[i]) {
                case ConflictResolution.Skip:
                    results[i] = new BatchItemResult(plan.Source, plan.Destination, BatchItemStatus.Skipped, null);
                    continue;
                case ConflictResolution.Rename:
                // A folder inside an archive cannot be merged - only the
                // shell can walk it - so "keep both" means a new name here.
                case ConflictResolution.Merge:
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
                    if (RecycleUndo.Of(_bin.Send(plan.Destination), _bin, _undo, _log) is { } restore) {
                        restores.Add(restore);
                    }
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

    /// <summary>
    /// Unpacks the sources the window would otherwise have to guess about,
    /// so their bytes can be compared with the files they would overwrite.
    /// Only same-size file pairs are worth it - anything else is already
    /// decided by kind or size, without reading a byte
    /// (<see cref="ConflictVerdict.ContentUndecided"/>).
    ///
    /// <para>
    /// Smallest first, up to <see cref="FileContentComparer.AutoCompareLimit"/>
    /// altogether: a single 60 MB pair must not cost the two 1 KB ones
    /// their answer, and unpacking everything a batch happens to collide
    /// with would make the dialog wait on the disk. What is left over stays
    /// as it was - decided on name, size and date.
    /// </para>
    ///
    /// <para>
    /// The copies go to the same scratch folders the preview pane and
    /// "Open" use, so an entry already unpacked costs nothing, and
    /// <c>TempFiles.Sweep</c> clears them a day later. An entry the shell
    /// will not give up (a password) simply keeps its old verdict.
    /// </para>
    /// </summary>
    private async Task UnpackForComparison(List<FileConflictInfo> infos, CancellationToken ct) {
        var candidates = new List<int>();
        for (int i = 0; i < infos.Count; i++) {
            var info = infos[i];
            if (info.Source.Kind == EntryKind.File
                && info.ExistingTarget.Kind == EntryKind.File
                && info.Source.Size is { } size
                && size == info.ExistingTarget.Size) {
                candidates.Add(i);
            }
        }

        // Two ceilings: bytes, and how many runs of the shell's engine the
        // dialog is made to wait for - each copy is a file operation of its
        // own, and a hundred tiny collisions would otherwise cost a hundred
        // of them before anything is asked.
        const int MaxUnpacks = 32;
        long budget = FileContentComparer.AutoCompareLimit;
        int unpacked = 0;
        foreach (int i in candidates.OrderBy(i => infos[i].Source.Size ?? 0)) {
            long size = infos[i].Source.Size ?? 0;
            if (size > budget || unpacked >= MaxUnpacks) {
                break;
            }
            budget -= size;
            unpacked++;

            try {
                string copy = await TempExtraction
                    .CopyOutAsync(_ns, _fs, _log, infos[i].Source.FullPath, ct)
                    .ConfigureAwait(false);
                infos[i] = infos[i] with { SourceReachable = true, ReadablePath = copy };
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                _log.Info($"Extract: cannot unpack {infos[i].Source.FullPath} for comparison - {ex.Message}");
            }
        }
    }

    /// <summary>
    /// How many files and bytes the queue holds, from the directories of
    /// the zips it comes out of; null when any part of it cannot be told -
    /// another kind of archive, a zip that will not open, a name not found
    /// in its directory - and the operation counts the old way.
    /// </summary>
    private ExtractWeight? Weigh(IReadOnlyList<CopyOutItem> queue, CancellationToken ct) {
        var catalogs = new Dictionary<string, ZipCatalog>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        long bytes = 0;
        foreach (var item in queue) {
            if (_ns.ParseArchive(item.Path) is not { } archive
                || !string.Equals(Path.GetExtension(archive.Archive), ".zip", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            if (!catalogs.TryGetValue(archive.Archive, out var catalog)) {
                try {
                    using var stream = _fs.OpenRead(archive.Archive);
                    catalog = ZipCatalog.Read(stream, CultureInfo.CurrentCulture.TextInfo.OEMCodePage, ct);
                } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
                    _log.Info($"Extract: zip directory not read: {archive.Archive} ({ex.Message}) - counted by entries");

                    return null;
                }
                catalogs[archive.Archive] = catalog;
            }

            if (catalog.SizeOf(archive.Inner) is { } size) {
                files++;
                bytes += size;
            } else if (catalog.Census(archive.Inner, maxTypes: 0) is { } stats) {
                files += stats.Files;
                bytes += stats.TotalSize;
            } else {
                return null;
            }
        }

        return new ExtractWeight(files, bytes, path =>
            _ns.ParseArchive(path) is { } archive && catalogs.TryGetValue(archive.Archive, out var catalog)
                ? catalog.SizeOf(archive.Inner)
                : null);
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
                steps, OperationVerbs.Extract, Math.Max(1, extracted.Count)));
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
    /// kept in <paramref name="listings"/> for the batch: each one may be a
    /// big zip's whole catalogue read again.
    /// </summary>
    private FileConflictInfo BuildInfo(Plan plan, Dictionary<string, IReadOnlyList<FileSystemEntry>> listings) {
        string parent = Path.GetDirectoryName(plan.Source) ?? "";
        if (!listings.TryGetValue(parent, out var listing)) {
            listing = _ns.Enumerate(parent);
            listings[parent] = listing;
        }
        var source = listing.FirstOrDefault(e => string.Equals(e.FullPath, plan.Source, StringComparison.OrdinalIgnoreCase));

        // Nothing inside an archive can be opened through IFileSystem, so
        // the pair starts out decided on name, size and date alone.
        // UnpackForComparison then buys the bytes back for the file pairs
        // where they would settle the question; a folder keeps this
        // verdict, because merging one means walking it, and only the
        // shell can.
        return new FileConflictInfo(
            source ?? Unknown(plan.Source),
            _fs.GetEntry(plan.Destination) ?? Unknown(plan.Destination),
            SourceReachable: false);
    }

    private bool Exists(string path) => _fs.FileExists(path) || _fs.DirectoryExists(path);

    /// <summary>The number after the highest already there - the one rule, see <see cref="UniqueNames"/>.</summary>
    private string UniqueName(string desired) {
        return UniqueNames.Resolve(desired, Exists, folder => _fs.Enumerate(folder).Select(e => e.Name));
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


    /// <summary>The same, for the engine's work units.</summary>
    private sealed class InlineWorkProgress : IProgress<CopyOutWork> {
        private readonly Action<CopyOutWork> _report;

        public InlineWorkProgress(Action<CopyOutWork> report) {
            _report = report;
        }

        public void Report(CopyOutWork value) => _report(value);
    }


    /// <summary>What an extraction holds, and what one file landed weighs (null for a folder).</summary>
    private sealed record ExtractWeight(int Files, long Bytes, Func<string, long?> SizeOf);


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
