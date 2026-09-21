using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Localization;
using Wander.Core.Logging;

namespace Wander.Core.Operations;

/// <summary>
/// A path an operation found held and waited for (PLAN AF, block 0): who
/// held it, how long the operation waited, whether it was let go.
/// </summary>
/// <param name="Path">What was held.</param>
/// <param name="Holder">Who, as the user is told: "Word (PID 812)", "Wander: thumbnail"; null when nobody could be named.</param>
/// <param name="Waited">How long the operation waited for it.</param>
/// <param name="Released">Let go within the wait: the operation went ahead.</param>
public sealed record BusyReport(string Path, string? Holder, TimeSpan Waited, bool Released);


/// <summary>
/// The item was left alone: an operation the user started earlier is still
/// working on it (PLAN AF: Wander never fights itself, it names the
/// operation in the way).
/// </summary>
public sealed class ClaimedByOperationException : IOException {
    public ClaimedByOperationException(string path, string verb)
        : base($"'{path}' is in use by another operation of Wander's ({verb})") {
        ItemPath = path;
        Verb = verb;
    }


    public string ItemPath { get; }

    /// <summary>The <see cref="OperationVerbs"/> key of the operation in the way.</summary>
    public string Verb { get; }
}


/// <summary>
/// What file operations share about held paths: the claims, the cheap probe
/// of a file, who to ask for a holder's name, how a wait is made. One per
/// application, like the operations themselves; <see cref="Begin"/> gives an
/// operation its own <see cref="BusyGate"/>.
/// </summary>
public sealed class HeldPaths {
    private readonly Func<BusyWait> _newWait;


    /// <param name="claims">Whose paths are whose.</param>
    /// <param name="probe">Asks a file cheaply; null waits only for what an operation reports in use.</param>
    /// <param name="locks">Names an outside holder; null leaves it unnamed.</param>
    /// <param name="newWait">The wait an operation gets, so a test does not have to sleep.</param>
    public HeldPaths(PathClaims claims, IFileBusyProbe? probe = null, IFileLockInspector? locks = null, Func<BusyWait>? newWait = null) {
        Claims = claims;
        Probe = probe;
        Locks = locks;
        _newWait = newWait ?? (() => new BusyWait());
    }


    public PathClaims Claims { get; }

    public IFileBusyProbe? Probe { get; }

    public IFileLockInspector? Locks { get; }


    /// <summary>A gate for one operation. Dispose it when the operation ends: that writes its line to the log.</summary>
    /// <param name="log">Where every wait and the operation's claim lookups are written.</param>
    /// <param name="own">The operation's own claim, if it made one - never in its own way.</param>
    public BusyGate Begin(ILogger log, IDisposable? own = null) {
        return new BusyGate(this, _newWait(), log, own);
    }
}


/// <summary>
/// One operation's dealings with held paths (PLAN AF, block 0, steps 4-5),
/// asked before each item it touches.
///
/// <list type="bullet">
///   <item>An operation of the user's claims the path: the item is not
///   touched, it fails with <see cref="ClaimedByOperationException"/> - the
///   user is told which operation, rather than having Wander fight itself.</item>
///   <item>A background reader of ours claims it: told to let go
///   (<see cref="PathClaims.Yield"/>), then waited for like anybody else -
///   and named as ours.</item>
///   <item>Held by anybody: waited for, one look every
///   <see cref="BusyWait.DefaultStep"/>, out of one budget for the whole
///   operation (<see cref="BusyWait"/>). Named once, at the first look that
///   finds it held; every wait goes to the log, and to the result as a
///   <see cref="BusyReport"/>.</item>
/// </list>
///
/// <para>
/// A file is asked with the probe before it is touched. A folder has no
/// probe - the operation itself is tried again while it answers "in use"
/// (<see cref="Retry"/>), and so is a batch the bin skips held items of
/// (<see cref="RetryMany"/>).
/// </para>
/// </summary>
public sealed class BusyGate : IDisposable {
    /// <summary>A claims lookup slower than this is worth a warning (PLAN block 0, step 5).</summary>
    private const double SlowLookupMs = 5;

    private readonly HeldPaths _held;
    private readonly BusyWait _wait;
    private readonly ILogger _log;
    private readonly IDisposable? _own;

    // Background readers told to let go of a path, by path: the name to give
    // if the path then turns out held.
    private readonly Dictionary<string, string> _yielded = new(StringComparer.OrdinalIgnoreCase);

    private int _lookups;
    private double _slowestMs;


    internal BusyGate(HeldPaths held, BusyWait wait, ILogger log, IDisposable? own) {
        _held = held;
        _wait = wait;
        _log = log;
        _own = own;
    }


    /// <summary>
    /// Before an item is touched: fails it when an operation of the user's is
    /// in the way, and tells background readers in the way to let go.
    /// </summary>
    /// <exception cref="ClaimedByOperationException">Another operation of the user's claims the path.</exception>
    public void Check(string path) {
        long started = Stopwatch.GetTimestamp();
        var covering = _held.Claims.Covering(path, _own);
        var operation = covering.FirstOrDefault(c => c.Kind == ClaimKind.UserOperation);
        var yielded = operation is null && covering.Count > 0 ? _held.Claims.Yield(path) : Array.Empty<PathClaim>();
        Count(Stopwatch.GetElapsedTime(started));

        if (operation is not null) {
            _log.Info($"Busy: {path} is being worked on by Wander ({operation.Owner}) - left alone");

            throw new ClaimedByOperationException(path, operation.Owner);
        }
        if (yielded.Count > 0) {
            _log.Info($"Busy: {path} - asked {string.Join(", ", yielded.Select(c => c.Owner))} to let go");
            _yielded[path] = yielded[0].Owner;
        }
    }

    /// <summary>
    /// Waits for a file the probe finds held. Null when it was free at the
    /// first look, or nothing can look.
    /// </summary>
    /// <exception cref="IOException">Still held when the budget ran out (<see cref="FileInUse"/>).</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled while waiting.</exception>
    public BusyReport? WaitForFile(string path, CancellationToken ct) {
        if (_held.Probe is not { } probe) {
            return null;
        }

        string? holder = null;
        bool first = true;
        var result = _wait.UntilFree(() => {
            bool busy = probe.IsBusy(path);
            if (busy && first) {
                holder = NameHolder(path);
            }
            first = false;

            return busy;
        }, ct);
        if (!result.WasBusy) {
            return null;
        }

        var report = Report(new BusyReport(path, holder, result.Waited, result.Free));
        if (!result.Free) {
            throw FileInUse.Error(path);
        }

        return report;
    }

    /// <summary>
    /// Runs <paramref name="operation"/>, and again every step while it
    /// answers "in use", within the budget - for a folder, which has no
    /// probe. Null when it went through at the first try.
    /// </summary>
    /// <exception cref="IOException">Still "in use" when the budget ran out - the operation's own exception.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled while waiting.</exception>
    public BusyReport? Retry(string path, Action operation, CancellationToken ct) {
        Exception? inUse = null;
        string? holder = null;
        var result = _wait.UntilFree(() => {
            try {
                operation();

                return false;
            } catch (Exception ex) when (FileInUse.Is(ex)) {
                holder ??= NameHolder(path);
                inUse = ex;

                return true;
            }
        }, ct);
        if (!result.WasBusy) {
            return null;
        }

        var report = Report(new BusyReport(path, holder, result.Waited, result.Free));
        if (!result.Free) {
            ExceptionDispatchInfo.Throw(inUse!);
        }

        return report;
    }

    /// <summary>
    /// A batch whose held items come back "in use" instead of being waited
    /// for one by one - the recycle bin's way: <paramref name="attempt"/>
    /// runs over every item, then over the ones it answered "in use" with,
    /// all together, every step of the wait while the budget lasts. A cancel
    /// ends the wait without throwing: what the batch already did stays done.
    /// </summary>
    /// <param name="paths">The items, by index.</param>
    /// <param name="attempt">Does the items at the indices given; returns the indices it found held.</param>
    /// <returns>A report for every item that was held at the first attempt, by index.</returns>
    public IReadOnlyDictionary<int, BusyReport> RetryMany(
        IReadOnlyList<string> paths, Func<IReadOnlyList<int>, IReadOnlyList<int>> attempt, CancellationToken ct) {
        var held = attempt(Enumerable.Range(0, paths.Count).ToList());
        if (held.Count == 0) {
            return new Dictionary<int, BusyReport>();
        }

        var holders = held.ToDictionary(i => i, i => NameHolder(paths[i]));
        var released = new Dictionary<int, TimeSpan>();
        var left = _wait.Remaining;
        bool first = true;
        try {
            _wait.UntilFree(() => {
                if (!first) {
                    var still = attempt(held);
                    var waited = left - _wait.Remaining;
                    foreach (int i in held.Except(still)) {
                        released[i] = waited;
                    }
                    held = still;
                }
                first = false;

                return held.Count > 0;
            }, ct);
        } catch (OperationCanceledException) {
            // Stopped waiting; the items still held are reported held.
        }

        var reports = new Dictionary<int, BusyReport>(holders.Count);
        foreach (var (i, holder) in holders) {
            bool free = released.TryGetValue(i, out var waited);
            reports[i] = Report(new BusyReport(paths[i], holder, free ? waited : left - _wait.Remaining, free));
        }

        return reports;
    }

    /// <summary>The operation's line in the log: how many claim lookups it made, the slowest, how big the table was.</summary>
    public void Dispose() {
        if (_lookups == 0) {
            return;
        }

        string line = $"Claims: {_lookups} lookups, slowest {_slowestMs:0.###} ms, table {_held.Claims.Count} paths";
        if (_slowestMs > SlowLookupMs) {
            _log.Warn(line);
        } else {
            _log.Info(line);
        }
    }


    private void Count(TimeSpan took) {
        _lookups++;
        _slowestMs = Math.Max(_slowestMs, took.TotalMilliseconds);
    }

    /// <summary>
    /// Who holds <paramref name="path"/>: a reader of ours that was told to
    /// let go, then any claim of ours on it, then whoever the platform names.
    /// </summary>
    private string? NameHolder(string path) {
        string? ours = _yielded.GetValueOrDefault(path);
        if (ours is null) {
            long started = Stopwatch.GetTimestamp();
            ours = _held.Claims.Covering(path, _own).FirstOrDefault()?.Owner;
            Count(Stopwatch.GetElapsedTime(started));
        }
        if (ours is not null) {
            return Text.Format("HolderWander", Text.Get(ours));
        }

        var lockers = _held.Locks?.WhoIsLocking(path);

        return lockers is { Count: > 0 } ? FileLockInfo.Describe(lockers) : null;
    }

    private BusyReport Report(BusyReport report) {
        _log.Info(
            $"Busy: {report.Path} held by {report.Holder ?? "somebody unnamed"} - " +
            $"{(report.Released ? "let go" : "still held")} after {report.Waited.TotalMilliseconds:0} ms");

        return report;
    }
}
