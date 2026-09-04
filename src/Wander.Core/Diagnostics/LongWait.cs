using System.Diagnostics;
using Wander.Core.Logging;

namespace Wander.Core.Diagnostics;

/// <summary>
/// Says in the log when something in the background is taking long, and
/// how long it took when it finally finished. For the waits a person sees
/// as a spinner - an archive being listed or unpacked through the shell, a
/// folder on a sleeping disk - where the log otherwise shows nothing
/// between "started" and a first screen that never comes.
///
/// <para>
/// Two lines at most per wait, both INFO: <c>SLOW wait: what - still
/// running after N s</c> once the threshold has passed, and <c>SLOW done:
/// what - took N s</c> when it ends, whichever way it ends. A wait that
/// finishes in time writes nothing. Not PerfLog: that aggregates what was
/// fast enough to happen many times a second, this names one thing that is
/// still not done.
/// </para>
/// </summary>
public static class LongWait {
    /// <summary>How long a background wait may take before the log hears of it.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromSeconds(5);


    /// <summary>
    /// Awaits <paramref name="task"/> and returns its result; the lines
    /// above are the only side effect. Faults and cancellations pass
    /// through untouched, after the "done" line if one is owed.
    /// </summary>
    public static async Task<T> WatchAsync<T>(Task<T> task, ILogger log, string what, TimeSpan? threshold = null) {
        var limit = threshold ?? Threshold;
        var started = Stopwatch.StartNew();

        using var timer = new CancellationTokenSource();
        var finished = await Task.WhenAny(task, Task.Delay(limit, timer.Token)).ConfigureAwait(false);
        if (finished == task) {
            timer.Cancel();

            return await task.ConfigureAwait(false);
        }

        log.Info($"SLOW wait: {what} - still running after {limit.TotalSeconds:0} s");
        try {
            return await task.ConfigureAwait(false);
        } finally {
            log.Info($"SLOW done: {what} - took {started.Elapsed.TotalSeconds:0.0} s");
        }
    }

    /// <summary>The same for a task without a result.</summary>
    public static async Task WatchAsync(Task task, ILogger log, string what, TimeSpan? threshold = null) {
        await WatchAsync(Wrap(task), log, what, threshold).ConfigureAwait(false);
    }


    private static async Task<bool> Wrap(Task task) {
        await task.ConfigureAwait(false);

        return true;
    }
}
