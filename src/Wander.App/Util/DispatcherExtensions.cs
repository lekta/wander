using System.Windows.Threading;

namespace Wander.App.Util;

/// <summary>
/// The one way to get onto the UI thread from code that may or may not
/// already be on it.
///
/// <para>
/// Written out by hand, the check reads as a choice — and a caller who
/// forgets it gets a crash that reproduces once in a hundred runs, on
/// whichever thread the file operation happened to finish on. There are
/// exactly two things anyone wants here, and they differ in a way that
/// must stay visible at the call site: <see cref="Post"/> does not wait,
/// <see cref="Ask{T}"/> blocks until the UI thread answers.
/// </para>
/// </summary>
public static class DispatcherExtensions {
    /// <summary>
    /// Runs the action on the UI thread: right now if the caller is already
    /// there, queued otherwise. Never blocks — for notifications, command
    /// re-evaluation, property change signals.
    /// </summary>
    public static void Post(this Dispatcher dispatcher, Action action) {
        if (dispatcher.CheckAccess()) {
            action();
        } else {
            dispatcher.BeginInvoke(action);
        }
    }


    /// <summary>
    /// Asks the UI thread a question and waits for the answer — a modal
    /// dialog opened from a background operation. Blocks the calling thread
    /// when it is not the UI one, so never call it while holding a lock the
    /// UI thread might want.
    /// </summary>
    public static T Ask<T>(this Dispatcher dispatcher, Func<T> question) {
        return dispatcher.CheckAccess() ? question() : dispatcher.Invoke(question);
    }
}
