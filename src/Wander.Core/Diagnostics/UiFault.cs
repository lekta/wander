namespace Wander.Core.Diagnostics;

/// <summary>What the app does with an exception the dispatcher did not catch.</summary>
public enum UiFaultAnswer {
    /// <summary>Offer the crash report and go on: a fault in one click is not the end of the window.</summary>
    Offer,

    /// <summary>Close the session with exit code 1: nobody is there to read a report (a smoke run, the harness).</summary>
    ShutDown,

    /// <summary>
    /// Leave the process now: the session was ending already, and a fault
    /// swallowed there leaves a process with no window holding its files,
    /// its state and a log-off Windows is waiting on (audit L3).
    /// </summary>
    Exit,
}

/// <summary>The rule behind the dispatcher's last-resort handler.</summary>
public static class UiFault {
    /// <param name="appEnding">The app ended the session itself: a smoke countdown, the harness, Windows logging off.</param>
    /// <param name="dispatcherEnding">WPF's own shutdown has started (<c>Dispatcher.HasShutdownStarted</c>).</param>
    /// <param name="headless">No one at the screen (<c>App.Headless</c>).</param>
    public static UiFaultAnswer Answer(bool appEnding, bool dispatcherEnding, bool headless) {
        if (appEnding || dispatcherEnding) {
            return UiFaultAnswer.Exit;
        }

        return headless ? UiFaultAnswer.ShutDown : UiFaultAnswer.Offer;
    }
}
