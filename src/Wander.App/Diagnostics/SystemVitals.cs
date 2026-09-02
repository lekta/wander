using System.Diagnostics;
using Wander.Core.Logging;

namespace Wander.App.Diagnostics;

/// <summary>
/// One line of process vitals in the session log, every five seconds and
/// at every stall.
///
/// <para>
/// PerfLog says where a slow moment went; this says what the process
/// looked like while it happened. Read on its own a single line means
/// little - the numbers are only interesting as a shape over a session: a
/// working set that keeps climbing, a handle count that never comes back
/// down, a gen2 count that ticks once per folder, CPU that stays busy
/// after the window went quiet. That is what a soak run is looking for,
/// and it is the one thing a log of durations cannot show.
/// </para>
///
/// <para>
/// Written from <see cref="UiStallWatch"/>'s heartbeat, off the UI thread,
/// so reading the numbers cannot itself be the stall. Cost: one
/// <c>Process.Refresh</c>, one string and one thread-collection snapshot
/// every five seconds - measurable only if you go looking for it.
/// </para>
/// </summary>
public static class SystemVitals {
    /// <summary>
    /// How often a line is written when nothing is wrong. Long enough to
    /// stay out of the way of the lines that say something happened, short
    /// enough that a half-minute of trouble has half a dozen samples in it.
    /// </summary>
    private const long IntervalMs = 5000;

    private static readonly object _lock = new();

    /// <summary>
    /// Taken once: <c>GetCurrentProcess</c> opens a handle every time it is
    /// called, and a diagnostic that leaks handles would be measuring
    /// itself.
    /// </summary>
    private static readonly Process _process = Process.GetCurrentProcess();

    private static ILogger? _log;
    private static long _lastLineMs;
    private static long _lastAllocated;
    private static TimeSpan _lastCpu;
    private static long _lastCpuAtMs;


    /// <summary>
    /// Points the lines somewhere. Until this is called both entry points
    /// do nothing, which is what tests and the offline commands want.
    /// </summary>
    public static void Start(ILogger log) {
        lock (_lock) {
            _log = log;
            _lastLineMs = Environment.TickCount64;
            _lastAllocated = GC.GetTotalAllocatedBytes();
            _lastCpu = _process.TotalProcessorTime;
            _lastCpuAtMs = Environment.TickCount64;
        }
    }


    /// <summary>Writes a line if five seconds have passed. Called from the heartbeat.</summary>
    public static void Tick() {
        lock (_lock) {
            if (Environment.TickCount64 - _lastLineMs >= IntervalMs) {
                WriteLocked();
            }
        }
    }

    /// <summary>Writes a line now, whatever the interval says. Called when the window has just stalled.</summary>
    public static void Sample() {
        lock (_lock) {
            WriteLocked();
        }
    }


    private static void WriteLocked() {
        long now = Environment.TickCount64;
        _lastLineMs = now;
        if (_log is null) {
            return;
        }

        _process.Refresh();

        // CPU as a share of one core's worth of wall time, so 100 % means
        // the process is using every core it has - the same arithmetic the
        // harness's sampler does, so the two can be compared.
        var cpu = _process.TotalProcessorTime;
        long wall = now - _lastCpuAtMs;
        double cpuPercent = wall > 0
            ? (cpu - _lastCpu).TotalMilliseconds / wall / Environment.ProcessorCount * 100
            : 0;
        _lastCpu = cpu;
        _lastCpuAtMs = now;

        long allocated = GC.GetTotalAllocatedBytes();
        long allocatedSince = allocated - _lastAllocated;
        _lastAllocated = allocated;

        // GenerationInfo is gen0 / gen1 / gen2 / LOH / POH; the large
        // object heap is the one worth a column, because that is where
        // decoded bitmaps land and it is not compacted.
        var gc = GC.GetGCMemoryInfo();
        long loh = gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0;

        _log.Info(
            $"SYS ws={Mb(_process.WorkingSet64)} private={Mb(_process.PrivateMemorySize64)} " +
            $"gen={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} " +
            $"alloc=+{Mb(allocatedSince)} loh={Mb(loh)} " +
            $"handles={_process.HandleCount} threads={_process.Threads.Count} cpu={cpuPercent:F1}");
    }

    private static long Mb(long bytes) {
        return bytes / (1024 * 1024);
    }
}
