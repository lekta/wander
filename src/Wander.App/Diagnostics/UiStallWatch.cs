using System.Diagnostics;
using System.Windows.Threading;
using Wander.Core.Diagnostics;
using Wander.Core.Logging;

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

    // How often the watch looks up while the window has not answered yet.
    private const int SilenceCheckMs = 250;

    private static Thread? _worker;


    /// <param name="dispatcher">The window's thread, the one watched.</param>
    /// <param name="isActive">
    /// Whether a window of Wander is active - asked from the watch's thread,
    /// so a field the window keeps, not a property of it. In the background
    /// a stop is the system waking an idle thread, not work (audit
    /// 2026-11-18, L6): not written, and the pulse slows to a minute (L7).
    /// </param>
    public static void Start(Dispatcher dispatcher, Func<bool> isActive) {
        if (_worker is not null) {
            return;
        }

        _worker = new Thread(() => Loop(dispatcher, isActive)) {
            IsBackground = true,
            Name = "wander-ui-stall-watch",
        };
        _worker.Start();
    }


    private static void Loop(Dispatcher dispatcher, Func<bool> isActive) {
        while (true) {
            var waited = Stopwatch.StartNew();
            // Input priority: queued behind everything the window is
            // actually doing, so the wait *is* the busy time. Waited for in
            // slices, not without end: a window that does not answer at all
            // is said from here while it lasts, whatever its state - a
            // freeze ended in the task manager used to leave the log simply
            // stopping (audit 2026-11-18, L5).
            var ping = dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
            int said = 0;
            try {
                while (!ping.Task.Wait(SilenceCheckMs)) {
                    if (UiWatchRules.SilenceToSay(waited.Elapsed, said) is { } mark) {
                        said = mark;
                        Log.Current.Warn($"UI not responding for {mark} s");
                        SystemVitals.Sample();
                    }
                }
            } catch (AggregateException) {
                // The dispatcher is shutting down: the ping was dropped.
                return;
            }
            if (ping.Status != DispatcherOperationStatus.Completed) {
                return;
            }

            bool active = isActive();
            double elapsed = waited.Elapsed.TotalMilliseconds;
            if (elapsed >= StallMs && active) {
                PerfLog.Note("ui.stall", elapsed);
                // What the process looked like while the window was
                // stuck. Sampled here rather than on the interval so
                // the numbers belong to the stall, not to whatever
                // came after it.
                SystemVitals.Sample();
            }

            PerfLog.Tick();
            PerfCounters.Tick();
            SystemVitals.Tick(UiWatchRules.PulseInterval(active));
            Thread.Sleep(PingMs);
        }
    }
}
