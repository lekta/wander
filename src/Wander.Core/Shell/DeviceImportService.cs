using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Undo;

namespace Wander.Core.Shell;

/// <summary>
/// Copying off a phone or a camera onto the disk (PLAN Y4c): the
/// obligations of every file operation - the guard on the target, a line
/// in the log, the conflict window, progress that can be cancelled, an
/// undo step - carried out against a source <see cref="IFileSystem"/>
/// cannot read, and with what a copy off a card owes besides (OPERATIONS,
/// "Чего ждёт фотограф"): each file written whole under a temporary name,
/// read back, dated as on the device, a transient error tried again.
///
/// <para>
/// The shape of <see cref="ExtractionService"/>, not its engine: here the
/// bytes come file by file through Wander's own loop
/// (<see cref="IFileSystem.WriteFromStream"/>), which is what lets a device
/// that goes away in the middle be waited for (PLAN Y4d): the operation
/// pauses, asks every two seconds whether the device is back, and on its
/// return copies the file it was on again from the start - the device's
/// objects are found anew by their names, its ids are new. Done files are
/// not touched again. Not back within <see cref="WaitForDevice"/> - that
/// file and the rest fail, and "Повторить" is there for when it is.
/// </para>
///
/// <para>
/// A name taken at the target is asked about once, for the whole list. A
/// folder merged into one already there takes in what is new: a file of
/// the same size and date is the same file and is passed over, one that
/// differs gets "(1)" - nothing is replaced without being asked.
/// </para>
/// </summary>
public sealed class DeviceImportService {
    /// <summary>How long a copy waits for its device to come back (decision of 2026-10-19).</summary>
    public static readonly TimeSpan WaitForDevice = TimeSpan.FromMinutes(10);

    /// <summary>How often the device is asked whether it is back.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);

    /// <summary>The pause reasons, as keys into the app's strings.</summary>
    public const string PausedUnplugged = "ProgressWaitingForDevice";

    public const string PausedLocked = "ProgressWaitingForUnlock";

    /// <summary>ERROR_DEVICE_NOT_CONNECTED as an HRESULT: what a copy off a device that is not there fails with.</summary>
    private const int NotConnected = unchecked((int)0x8007048F);


    private readonly IDeviceContent _devices;
    private readonly IFileSystem _fs;
    private readonly IRecycleBin _bin;
    private readonly UndoService _undo;
    private readonly OperationTracker _tracker;
    private readonly ILogger _log;
    private readonly PathClaims _claims;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTime> _now;
    private readonly RetryPolicy _retry;


    /// <param name="claims">Where an import claims what it writes; null keeps them to itself.</param>
    /// <param name="delay">The pause between two looks at the device, and before a transient error is tried again; a test passes one that returns at once.</param>
    /// <param name="now">The clock the wait for the device is measured on.</param>
    /// <param name="retry">How often a transient error is tried again.</param>
    public DeviceImportService(
        IDeviceContent devices, IFileSystem fs, IRecycleBin bin, UndoService undo, OperationTracker tracker, ILogger log,
        PathClaims? claims = null, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTime>? now = null,
        RetryPolicy? retry = null) {
        _devices = devices;
        _fs = fs;
        _bin = bin;
        _undo = undo;
        _tracker = tracker;
        _log = log;
        _claims = claims ?? new PathClaims();
        _delay = delay ?? Task.Delay;
        _now = now ?? (() => DateTime.UtcNow);
        _retry = retry ?? RetryPolicy.Default;
    }


    /// <summary>
    /// Copies <paramref name="sources"/> - files and folders on one or more
    /// devices - into <paramref name="targetFolder"/>.
    /// </summary>
    /// <param name="verify">Read each copy back (the setting "Проверять скопированное").</param>
    /// <returns>One result per source, in the order given: the shape a copy reports, for the status bar and the record.</returns>
    /// <exception cref="IOException">The target folder is in the Windows tree (<see cref="SystemPathGuard.MayWriteInto"/>).</exception>
    public async Task<IReadOnlyList<BatchItemResult>> ImportAsync(
        IReadOnlyList<string> sources, string targetFolder, IConflictResolver resolver, bool verify, CancellationToken ct) {

        if (!SystemPathGuard.MayWriteInto(targetFolder, out string guardReason)) {
            _log.Warn($"Import refused: {targetFolder} ({guardReason})");

            throw new IOException(guardReason);
        }

        var results = new BatchItemResult?[sources.Count];
        var plans = new List<Plan>(sources.Count);
        for (int i = 0; i < sources.Count; i++) {
            string source = sources[i];
            var entry = await Task.Run(() => _devices.GetEntry(source), ct).ConfigureAwait(false);
            if (entry is null) {
                // Not there: gone from a phone that is there, or the phone
                // itself is not - two things to say.
                var state = await Task.Run(() => _devices.StateOf(source), ct).ConfigureAwait(false);
                Exception why = state == DeviceState.Ready
                    ? new FileNotFoundException("Not found on the device", source)
                    : new IOException("The device is not connected", NotConnected);
                results[i] = new BatchItemResult(source, Path.Combine(targetFolder, NameOf(source)), BatchItemStatus.Failed, why);
                continue;
            }

            plans.Add(new Plan(i, entry, Path.Combine(targetFolder, entry.Name)));
        }

        var restores = new List<IUndoableAction>();
        var created = new List<string>();
        try {
            if (!ResolveConflicts(plans, results, restores, resolver)) {
                return Filled(results, sources, targetFolder);
            }

            var queue = plans.Where(p => results[p.Index] is null).ToList();
            if (queue.Count == 0) {
                return Filled(results, sources, targetFolder);
            }

            using var claim = _claims.Claim(queue.Select(p => p.Destination), ClaimKind.UserOperation, OperationVerbs.Copy);
            using var operation = _tracker.Begin(OperationVerbs.Copy, queue.Count, totalBytes: 0, token: ct);
            operation.SetWeighing(true);
            long total = 0;
            foreach (var plan in queue) {
                total += await Task.Run(() => Weigh(plan, ct), ct).ConfigureAwait(false);
            }
            operation.SetTotalBytes(total);
            operation.SetWeighing(false);

            await CopyAll(queue, results, created, operation, verify, ct).ConfigureAwait(false);

            var items = new List<OperationItem>(results.Length);
            foreach (var r in Filled(results, sources, targetFolder)) {
                items.Add(new OperationItem(r.Source, r.FinalDestination, r.Status, r.Error, r.Attempts, r.Verified, r.Engine));
            }
            var record = operation.Finish(items, targetFolder);
            if (record is not null) {
                _log.Info($"Import done: {record.Done} of {record.Total} item(s), {record.Bytes} bytes, {record.Elapsed.TotalSeconds:F1} s; " +
                    $"failed {record.Failed}, skipped {record.Skipped}, verified {record.Verified}");
            }
        } finally {
            PushUndo(restores, created);
        }

        return Filled(results, sources, targetFolder);
    }


    // --- Before the copy ------------------------------------------------

    /// <summary>
    /// Asks about every name taken at the target, all at once, and carries
    /// out what the answers mean before a byte moves.
    /// </summary>
    /// <returns>False when the user cancelled the whole batch.</returns>
    private bool ResolveConflicts(List<Plan> plans, BatchItemResult?[] results, List<IUndoableAction> restores, IConflictResolver resolver) {
        var colliding = plans.Where(p => Exists(p.Destination)).ToList();
        if (colliding.Count == 0) {
            return true;
        }

        var infos = colliding
            .Select(p => new FileConflictInfo(
                p.Source, _fs.GetEntry(p.Destination) ?? Unknown(p.Destination),
                SourceReachable: false, SameBySizeAndDate: true))
            .ToList();
        var replies = resolver.ResolveAll(new ConflictRequest(infos, plans.Count));
        if (replies is null || replies.Any(r => r.Resolution == ConflictResolution.Cancel)) {
            _log.Info($"Import cancelled before start ({plans.Count} items, {infos.Count} conflicts)");
            foreach (var plan in plans) {
                results[plan.Index] ??= new BatchItemResult(plan.Source.FullPath, plan.Destination, BatchItemStatus.Cancelled, null);
            }

            return false;
        }

        var byPath = replies.ToDictionary(r => r.Conflict.Source.FullPath, r => r.Resolution, StringComparer.OrdinalIgnoreCase);
        foreach (var plan in colliding) {
            var answer = byPath.TryGetValue(plan.Source.FullPath, out var resolution)
                ? resolution
                : throw new InvalidOperationException($"The conflict resolver left {plan.Source.FullPath} unanswered.");
            bool bothFolders = plan.Source.Kind == EntryKind.Directory && _fs.DirectoryExists(plan.Destination);
            switch (answer) {
                case ConflictResolution.Skip:
                    results[plan.Index] = new BatchItemResult(plan.Source.FullPath, plan.Destination, BatchItemStatus.Skipped, null);
                    break;
                case ConflictResolution.Merge when bothFolders:
                    plan.Status = BatchItemStatus.Merged;
                    break;
                case ConflictResolution.Rename:
                case ConflictResolution.Merge:
                    plan.Destination = UniqueName(plan.Destination);
                    plan.Status = BatchItemStatus.Renamed;
                    break;
                case ConflictResolution.Replace:
                    // Into the bin, never into oblivion: the copy finds the
                    // way clear, Ctrl+Z brings the original back.
                    if (SystemPathGuard.IsProtected(plan.Destination, out string reason)) {
                        results[plan.Index] = new BatchItemResult(
                            plan.Source.FullPath, plan.Destination, BatchItemStatus.Failed, new IOException(reason));
                        break;
                    }
                    restores.Add(new DeleteAction(_bin, _bin.Send(plan.Destination)));
                    plan.Status = BatchItemStatus.Replaced;
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// What a source holds, walked off the device: its files with where each
    /// lands, and the folders to make. The bytes to come, for the bar.
    /// </summary>
    private long Weigh(Plan plan, CancellationToken ct) {
        if (plan.Source.Kind != EntryKind.Directory) {
            plan.Files.Add(new Piece(plan.Source, plan.Destination));

            return plan.Source.Size ?? 0;
        }

        long bytes = 0;
        var pending = new Stack<(string From, string To)>();
        pending.Push((plan.Source.FullPath, plan.Destination));
        while (pending.Count > 0) {
            ct.ThrowIfCancellationRequested();
            var (from, to) = pending.Pop();
            plan.Folders.Add(to);
            foreach (var entry in _devices.Enumerate(from, ct)) {
                string target = Path.Combine(to, entry.Name);
                if (entry.Kind == EntryKind.Directory) {
                    pending.Push((entry.FullPath, target));
                } else {
                    plan.Files.Add(new Piece(entry, target));
                    bytes += entry.Size ?? 0;
                }
            }
        }

        return bytes;
    }


    // --- The copy -------------------------------------------------------

    private async Task CopyAll(
        List<Plan> queue, BatchItemResult?[] results, List<string> created, IOperationHandle operation, bool verify,
        CancellationToken ct) {
        var options = new CopyOptions(Verify: verify);
        Exception? deviceLost = null;

        foreach (var plan in queue) {
            if (ct.IsCancellationRequested) {
                results[plan.Index] = new BatchItemResult(plan.Source.FullPath, plan.Destination, BatchItemStatus.Cancelled, null);
                continue;
            }
            if (deviceLost is not null) {
                // The device did not come back: what is left fails with it,
                // without each file waiting its own ten minutes.
                results[plan.Index] = new BatchItemResult(plan.Source.FullPath, plan.Destination, BatchItemStatus.Failed, deviceLost);
                continue;
            }

            operation.SetCurrentPath(plan.Source.FullPath);
            var outcome = await CopyPlan(plan, created, operation, options, ct).ConfigureAwait(false);
            deviceLost = outcome.DeviceLost;
            results[plan.Index] = new BatchItemResult(
                plan.Source.FullPath, plan.Destination, outcome.Status, outcome.Error,
                Attempts: outcome.Attempts, Verified: outcome.Verified, Engine: outcome.Copied ? CopyEngine.Own : CopyEngine.None);
            operation.Advance(plan.Source.FullPath);
            _log.Info($"Import: {plan.Source.FullPath} -> {plan.Destination} [{outcome.Status}]");
        }
    }

    /// <summary>
    /// One source: a file, or a folder with everything in it. What undo
    /// takes away is the top of what is new: a folder made here carries
    /// everything under it into the bin, so neither its files nor its
    /// subfolders are steps of their own; a file let into a folder that was
    /// there before is.
    /// </summary>
    private async Task<Outcome> CopyPlan(Plan plan, List<string> created, IOperationHandle operation, CopyOptions options, CancellationToken ct) {
        var made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            foreach (string folder in plan.Folders) {
                if (_fs.DirectoryExists(folder)) {
                    continue;
                }

                _fs.CreateDirectory(folder);
                made.Add(folder);
                if (!made.Contains(Path.GetDirectoryName(folder) ?? "")) {
                    created.Add(folder);
                }
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _log.Error($"Import: cannot make {plan.Destination}", ex);

            return new Outcome(BatchItemStatus.Failed, ex);
        }

        int attempts = 1;
        bool verified = options.Verify;
        bool copied = false;
        foreach (var piece in plan.Files) {
            string destination = piece.Destination;
            if (plan.Status == BatchItemStatus.Merged && Exists(destination)) {
                // Into a folder already there: the same file is passed over,
                // another one under that name comes in beside it.
                if (_fs.GetEntry(destination) is { } there && IsSameFile(piece.Source, there)) {
                    continue;
                }
                destination = UniqueName(destination);
            }

            FileOutcome result;
            try {
                result = await CopyFile(piece.Source, destination, operation, options, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                return new Outcome(BatchItemStatus.Cancelled, null, attempts);
            }

            attempts = Math.Max(attempts, result.Attempts);
            if (result.Error is not null) {
                return new Outcome(BatchItemStatus.Failed, result.Error, attempts, false, copied, result.DeviceLost ? result.Error : null);
            }

            copied = true;
            verified &= result.Verified;
            if (!made.Contains(Path.GetDirectoryName(destination) ?? "")) {
                created.Add(destination);
            }
        }

        return new Outcome(plan.Status, null, attempts, verified && copied, copied);
    }

    /// <summary>
    /// One file off the device: tried again on a transient error, waited
    /// for when the device went away. Throws only when cancelled.
    /// </summary>
    private async Task<FileOutcome> CopyFile(
        FileSystemEntry source, string destination, IOperationHandle operation, CopyOptions options, CancellationToken ct) {
        for (int attempt = 1; ; attempt++) {
            long reported = 0;
            var progress = new InlineProgress(delta => {
                reported += delta;
                operation.AdvanceBytes(delta);
            });
            try {
                var report = await Task.Run(() => {
                    using var stream = _devices.OpenRead(source.FullPath);
                    long length = source.Size ?? stream.Length;

                    return _fs.WriteFromStream(stream, length, destination, Dated(source), progress, ct, options);
                }, ct).ConfigureAwait(false);
                operation.SetAttempt(0);

                return new FileOutcome(null, attempt, report.Verified);
            } catch (OperationCanceledException) {
                operation.AdvanceBytes(-reported);

                throw;
            } catch (Exception ex) {
                operation.AdvanceBytes(-reported);
                var state = await Task.Run(() => _devices.StateOf(source.FullPath), CancellationToken.None).ConfigureAwait(false);
                if (state == DeviceState.Ready && DeviceErrors.IsGone(ex)) {
                    // The read broke the way a pulled cable breaks it, and the
                    // notice of the removal comes a moment after (stand Y4a):
                    // asked again after that moment.
                    await _delay(PollEvery, ct).ConfigureAwait(false);
                    state = await Task.Run(() => _devices.StateOf(source.FullPath), CancellationToken.None).ConfigureAwait(false);
                }
                // Only a device that is really not there is waited for: one
                // that is there and breaks a read anyway is an error like
                // any other, tried again and then named.
                if (state != DeviceState.Ready) {
                    _log.Warn($"Import: the device went away at {source.FullPath} ({ex.Message}) - waiting for it");
                    if (!await WaitForDeviceBack(source.FullPath, operation, ct).ConfigureAwait(false)) {
                        _log.Warn($"Import: the device did not come back in {WaitForDevice.TotalMinutes} min");

                        return new FileOutcome(ex, attempt, false, DeviceLost: true);
                    }

                    // Back: the same file again from its first byte, under a
                    // fresh look-up - the device's ids are new.
                    _log.Info($"Import: the device is back, {source.FullPath} again");
                    attempt = 0;
                    continue;
                }

                if (attempt < _retry.Attempts && TransientIo.IsTransient(ex)) {
                    operation.SetAttempt(attempt + 1);
                    _log.Info($"Import: {source.FullPath} - {ex.Message}; try {attempt + 1} of {_retry.Attempts}");
                    await _delay(_retry.DelayBefore(attempt + 1), ct).ConfigureAwait(false);
                    continue;
                }

                operation.SetAttempt(0);
                _log.Error($"Import failed: {source.FullPath} -> {destination}", ex);

                return new FileOutcome(ex, attempt, false);
            }
        }
    }

    /// <summary>
    /// The pause: the operation says what it waits for, and the device is
    /// asked every <see cref="PollEvery"/> until it is back with the file
    /// there, or <see cref="WaitForDevice"/> runs out.
    /// </summary>
    /// <returns>True when the device is back.</returns>
    private async Task<bool> WaitForDeviceBack(string source, IOperationHandle operation, CancellationToken ct) {
        var deadline = _now() + WaitForDevice;
        try {
            while (true) {
                var state = await Task.Run(() => _devices.StateOf(source), CancellationToken.None).ConfigureAwait(false);
                if (state == DeviceState.Ready
                    && await Task.Run(() => _devices.GetEntry(source), CancellationToken.None).ConfigureAwait(false) is not null) {
                    return true;
                }
                if (_now() >= deadline) {
                    return false;
                }

                operation.SetPaused(state == DeviceState.Locked ? PausedLocked : PausedUnplugged);
                await _delay(PollEvery, ct).ConfigureAwait(false);
            }
        } finally {
            operation.SetPaused(null);
        }
    }


    // --- Undo and odds and ends -----------------------------------------

    private void PushUndo(IReadOnlyList<IUndoableAction> restores, IReadOnlyList<string> created) {
        var steps = new List<IUndoableAction>(restores.Count + created.Count);
        steps.AddRange(restores);
        foreach (string path in created) {
            steps.Add(new CreateAction(_bin, path, OperationVerbs.Copy));
        }

        if (steps.Count == 0) {
            return;
        }

        _undo.Push(steps.Count == 1
            ? steps[0]
            : new CompositeAction($"import of {created.Count} items", steps, OperationVerbs.Copy, Math.Max(1, created.Count)));
    }

    /// <summary>Every slot filled: one the copy never reached was not attempted - cancelled.</summary>
    private static BatchItemResult[] Filled(BatchItemResult?[] results, IReadOnlyList<string> sources, string targetFolder) {
        var filled = new BatchItemResult[results.Length];
        for (int i = 0; i < results.Length; i++) {
            filled[i] = results[i] ?? new BatchItemResult(
                sources[i], Path.Combine(targetFolder, NameOf(sources[i])), BatchItemStatus.Cancelled, null);
        }

        return filled;
    }

    /// <summary>The same file - size and date (± 2 s) - as a copy off a device is judged (<see cref="FileConflictInfo.SameBySizeAndDate"/>).</summary>
    private static bool IsSameFile(FileSystemEntry source, FileSystemEntry there) {
        return there.Kind == EntryKind.File
            && source.Size is { } size && size == there.Size
            && source.ModifiedUtc != DateTime.MinValue
            && (source.ModifiedUtc - there.ModifiedUtc).Duration() <= ConflictVerdict.AgeTolerance;
    }

    /// <summary>The device's time of last change, when it gave one.</summary>
    private static DateTime? Dated(FileSystemEntry source) {
        return source.ModifiedUtc == DateTime.MinValue ? null : source.ModifiedUtc;
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
        return new FileSystemEntry(NameOf(path), path, EntryKind.File, null, DateTime.MinValue, false, false, false, false);
    }


    /// <summary>One source, and what has been decided about it so far.</summary>
    private sealed class Plan {
        public Plan(int index, FileSystemEntry source, string destination) {
            Index = index;
            Source = source;
            Destination = destination;
        }

        /// <summary>Its place among the sources - and in the results.</summary>
        public int Index { get; }

        public FileSystemEntry Source { get; }

        public string Destination { get; set; }

        public BatchItemStatus Status { get; set; } = BatchItemStatus.Ok;

        /// <summary>The files it holds - itself, for a file - with where each lands.</summary>
        public List<Piece> Files { get; } = new();

        /// <summary>The folders to make, the top one first.</summary>
        public List<string> Folders { get; } = new();
    }


    /// <summary>A file off the device and where it lands.</summary>
    private sealed record Piece(FileSystemEntry Source, string Destination);


    /// <summary>How one source went.</summary>
    /// <param name="Copied">At least one file of it was written.</param>
    /// <param name="DeviceLost">The device went and did not come back: the rest is not tried.</param>
    private sealed record Outcome(
        BatchItemStatus Status, Exception? Error, int Attempts = 1, bool Verified = false, bool Copied = false,
        Exception? DeviceLost = null);


    /// <summary>How one file went: no error - it is there.</summary>
    private sealed record FileOutcome(Exception? Error, int Attempts, bool Verified, bool DeviceLost = false);


    /// <summary>Reports on the thread that calls it, unlike <see cref="Progress{T}"/>: the count is settled before the file is.</summary>
    private sealed class InlineProgress : IProgress<long> {
        private readonly Action<long> _report;

        public InlineProgress(Action<long> report) {
            _report = report;
        }

        public void Report(long value) => _report(value);
    }
}
