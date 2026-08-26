using System.Diagnostics;
using Wander.Core.Logging;

namespace Wander.Core.Diagnostics;

/// <summary>
/// Coarse "where did the frame go" instrumentation for the session log.
///
/// <para>
/// The rule is that a log nobody can read is not a log: this one only ever
/// writes what costs noticeable time. Measurements are summed into
/// one-second windows and a window is written out only for the categories
/// that were expensive in it — a slow scroll leaves a couple of lines a
/// second, a quiet app leaves nothing at all.
/// </para>
///
/// <para>
/// Names beginning with <c>bg.</c> are work on a background thread. They can
/// legitimately add up to more than a second per second (two shell calls run
/// at once) and they are not what makes the window stutter — they are here
/// to show whether thumbnails are slow to *arrive*, which feels like lag
/// without ever blocking the UI thread. Everything else is time the UI
/// thread spent not drawing.
/// </para>
///
/// <para>
/// Cost when nothing is slow: two timestamps and a dictionary lookup per
/// measurement, and no output. It can stay in the code.
/// </para>
/// </summary>
public static class PerfLog {
    /// <summary>How long a window of measurements is summed for.</summary>
    private const long WindowMs = 1000;

    /// <summary>
    /// A category is worth a line once its window costs this much. Sixteen
    /// frames' worth: below that the second was smooth and nobody wants to
    /// read about it.
    /// </summary>
    private const double NoisyTotalMs = 100;

    /// <summary>
    /// …or once a single call in it does. Two frames at 60 Hz — the point
    /// where one operation is a visible hitch rather than a busy second.
    /// </summary>
    private const double NoisyOnceMs = 33;

    private static readonly object _lock = new();
    private static readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private static readonly double _tickToMs = 1000.0 / Stopwatch.Frequency;

    private static ILogger? _log;
    private static long _windowStartMs;


    /// <summary>
    /// Points the log somewhere. Until this is called every measurement is
    /// taken and thrown away, which is what tests and offline harnesses want.
    /// </summary>
    public static void Start(ILogger log) {
        lock (_lock) {
            _log = log;
            _windowStartMs = Environment.TickCount64;
            _buckets.Clear();
        }
    }


    /// <summary>Times a block: <c>using (PerfLog.Measure("layout.measure")) { … }</c>.</summary>
    public static Scope Measure(string name) {
        return new Scope(name);
    }


    /// <summary>Records a duration measured elsewhere.</summary>
    public static void Note(string name, double milliseconds) {
        lock (_lock) {
            if (_log is null) {
                return;
            }

            if (!_buckets.TryGetValue(name, out var bucket)) {
                bucket = new Bucket();
                _buckets[name] = bucket;
            }
            bucket.Add(milliseconds);

            if (Environment.TickCount64 - _windowStartMs >= WindowMs) {
                FlushLocked();
            }
        }
    }


    /// <summary>
    /// Closes the window if it is old enough. Called from a heartbeat so a
    /// measurement taken just before the app went quiet still gets written
    /// out instead of waiting for the next one.
    /// </summary>
    public static void Tick() {
        lock (_lock) {
            if (Environment.TickCount64 - _windowStartMs >= WindowMs) {
                FlushLocked();
            }
        }
    }


    /// <summary>Writes out whatever the current window holds. For shutdown.</summary>
    public static void Flush() {
        lock (_lock) {
            FlushLocked();
        }
    }


    private static void FlushLocked() {
        _windowStartMs = Environment.TickCount64;
        if (_log is null || _buckets.Count == 0) {
            return;
        }

        foreach (var (name, bucket) in _buckets) {
            if (bucket.Total >= NoisyTotalMs || bucket.Max >= NoisyOnceMs) {
                _log.Info($"PERF {name}: {bucket.Total:F0} ms in {bucket.Count} calls, worst {bucket.Max:F1} ms");
            }
        }

        _buckets.Clear();
    }


    private sealed class Bucket {
        public int Count { get; private set; }

        public double Total { get; private set; }

        public double Max { get; private set; }


        public void Add(double ms) {
            Count++;
            Total += ms;
            if (ms > Max) {
                Max = ms;
            }
        }
    }


    /// <summary>
    /// The timing block itself. A struct, so measuring a hot path does not
    /// allocate on every call.
    /// </summary>
    public readonly struct Scope : IDisposable {
        private readonly string _name;
        private readonly long _start;


        internal Scope(string name) {
            _name = name;
            _start = Stopwatch.GetTimestamp();
        }


        public void Dispose() {
            Note(_name, (Stopwatch.GetTimestamp() - _start) * _tickToMs);
        }
    }
}
