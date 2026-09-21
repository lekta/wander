using System.Runtime.ExceptionServices;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Runs a piece of shell work on a thread of its own, in its own STA.
/// The bin's shell folder and the copy engine are apartment-threaded, and
/// they are asked for from pool threads (MTA): created there, the object
/// lands in the process-wide host STA, every call is marshalled to it, and
/// whatever else lives in that one apartment - the overlay lookups of the
/// icon loader - queues behind it (2026-09-18: four icon slots held for
/// 4.8 s by a cold recycle bin, the next folder with them). On its own
/// apartment the calls are direct and nobody waits.
///
/// <para>
/// Every COM object the work creates has to be released before it returns:
/// the apartment dies with the thread, and a wrapper left to the collector
/// would be released into an apartment that is no longer there.
/// </para>
/// </summary>
internal static class OwnApartment {
    public static T Run<T>(string threadName, Func<T> work) {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() => {
            try {
                result = work();
            } catch (Exception ex) {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        }) {
            IsBackground = true,
            Name = threadName,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();

        return result;
    }
}
