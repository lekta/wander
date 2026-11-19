namespace Wander.Core.Logging;

/// <summary>
/// A task started and not awaited - <c>_ = SomethingAsync()</c> - says how
/// it ended only to the finalizer: an exception in it reached the log as
/// <c>Unobserved task exception</c> at the next garbage collection, late,
/// without a name, or never (audit 2026-11-18, P10). Forgotten through
/// here, it is a line the moment it fails.
/// </summary>
public static class TaskForget {
    /// <summary>
    /// Lets <paramref name="task"/> run on its own: a failure is an ERROR
    /// with its stack and <paramref name="what"/> - the work it was; a
    /// cancellation is the caller's own doing and says nothing. A task that
    /// succeeds is not touched.
    /// </summary>
    public static void Forget(this Task task, ILogger log, string what) {
        task.ContinueWith(
            static (failed, state) => {
                var (log, what) = ((ILogger, string))state!;
                var error = failed.Exception!;
                log.Error($"{what} failed", error.InnerExceptions.Count == 1 ? error.InnerException : error);
            },
            (log, what),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
