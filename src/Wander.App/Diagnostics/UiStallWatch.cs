using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using Wander.Core.Diagnostics;

namespace Wander.App.Diagnostics;

/// <summary>
/// Measures how long the UI thread is busy, from outside it.
///
/// <para>
/// The thing the user calls "тормоза" is exactly this: a stretch where the
/// window cannot answer. Nothing running on the UI thread can time that —
/// it is the thread that is stuck — so a background thread asks the
/// dispatcher for a moment of its time every so often and reports how long
/// it had to wait. Anything past <see cref="StallMs"/> is a hitch a person
/// can feel; the individual probes in <see cref="PerfLog"/> then say what
/// the thread was doing.
/// </para>
///
/// <para>
/// The same heartbeat closes <see cref="PerfLog"/>'s window, so a slow
/// moment is written out while it is still interesting.
/// </para>
/// </summary>
public static class UiStallWatch {
    private const int PingMs = 200;

    /// <summary>
    /// How long the window has to be unresponsive before it is worth a line
    /// in the log. Shorter than this reads as a stutter at worst; longer is
    /// something a person notices and complains about.
    /// </summary>
    private const double StallMs = 150;

    private static Thread? _worker;


    public static void Start(Dispatcher dispatcher) {
        if (_worker is not null) {
            return;
        }

        _worker = new Thread(() => Loop(dispatcher)) {
            IsBackground = true,
            Name = "wander-ui-stall-watch",
        };
        _worker.Start();
    }


    private static void Loop(Dispatcher dispatcher) {
        while (true) {
            var waited = Stopwatch.StartNew();
            try {
                // Input priority: queued behind everything the window is
                // actually doing, so the wait *is* the busy time.
                dispatcher.Invoke(() => { }, DispatcherPriority.Input);
            } catch (TaskCanceledException) {
                return;
            } catch (OperationCanceledException) {
                return;
            }

            double elapsed = waited.Elapsed.TotalMilliseconds;
            if (elapsed >= StallMs) {
                PerfLog.Note("ui.stall", elapsed);
            }

            PerfLog.Tick();
            PerfCounters.Tick();
            Thread.Sleep(PingMs);
        }
    }
}
