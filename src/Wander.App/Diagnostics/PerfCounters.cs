using Wander.Core.Logging;

namespace Wander.App.Diagnostics;

/// <summary>
/// Counts next to <c>PerfLog</c>'s timings: how many containers a layout
/// pass built, reused or threw away (PLAN R1). Same one-second window, same
/// log, a different unit - and a different rule for writing: every window
/// that counted anything is written out, because a count of thirty says
/// something a total of thirty milliseconds does not. Numbers, not times,
/// so PerfLog's "is this noisy enough to print" thresholds do not apply.
///
/// <para>
/// Flushed from the same heartbeat as PerfLog (<see cref="UiStallWatch"/>),
/// so a pass that ran just before the app went quiet still gets its line.
/// Cost when nothing happens: nothing - the dictionary stays empty.
/// </para>
/// </summary>
public static class PerfCounters {
    private const long WindowMs = 1000;

    private static readonly object _lock = new();
    private static readonly Dictionary<string, (long Total, int Events)> _counts = new(StringComparer.Ordinal);

    private static ILogger? _log;
    private static long _windowStartMs;


    public static void Start(ILogger log) {
        lock (_lock) {
            _log = log;
            _windowStartMs = Environment.TickCount64;
            _counts.Clear();
        }
    }


    /// <summary>Adds <paramref name="count"/> to <paramref name="name"/> in the current window.</summary>
    public static void Add(string name, int count) {
        if (count == 0) {
            return;
        }

        lock (_lock) {
            if (_log is null) {
                return;
            }

            _counts.TryGetValue(name, out var bucket);
            _counts[name] = (bucket.Total + count, bucket.Events + 1);

            if (Environment.TickCount64 - _windowStartMs >= WindowMs) {
                FlushLocked();
            }
        }
    }


    /// <summary>Closes the window if it is old enough. Called from the heartbeat.</summary>
    public static void Tick() {
        lock (_lock) {
            if (Environment.TickCount64 - _windowStartMs >= WindowMs) {
                FlushLocked();
            }
        }
    }


    private static void FlushLocked() {
        _windowStartMs = Environment.TickCount64;
        if (_log is null || _counts.Count == 0) {
            return;
        }

        foreach (var (name, bucket) in _counts) {
            _log.Info($"COUNT {name}: {bucket.Total} in {bucket.Events} passes");
        }

        _counts.Clear();
    }
}
