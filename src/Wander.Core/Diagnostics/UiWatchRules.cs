namespace Wander.Core.Diagnostics;

/// <summary>
/// What the watch over the window's thread writes, and when (audit
/// 2026-11-18, L5-L7). The watch itself - a thread asking the dispatcher
/// for a moment - lives in the application; the rules are here, where a
/// test reaches them.
/// </summary>
public static class UiWatchRules {
    /// <summary>The pulse of <c>SYS</c> lines while a window of Wander is active.</summary>
    public static readonly TimeSpan ActivePulse = TimeSpan.FromSeconds(5);

    /// <summary>The pulse in the background: nothing happens there that a line every five seconds would tell.</summary>
    public static readonly TimeSpan BackgroundPulse = TimeSpan.FromSeconds(60);


    /// <summary>How often a <c>SYS</c> line is written, by whether a window of Wander is active.</summary>
    public static TimeSpan PulseInterval(bool active) {
        return active ? ActivePulse : BackgroundPulse;
    }


    /// <summary>
    /// The window's thread has not answered for <paramref name="silent"/>;
    /// <paramref name="lastSaid"/> seconds were already written (0 for
    /// none). The mark to write now - 2, 5, 10 s, then every 10 s - or null
    /// when the next one is not reached yet. A watch that wakes late writes
    /// the latest mark passed, not every one it missed.
    /// </summary>
    public static int? SilenceToSay(TimeSpan silent, int lastSaid) {
        double seconds = silent.TotalSeconds;
        int mark = seconds < 2 ? 0
            : seconds < 5 ? 2
            : seconds < 10 ? 5
            : (int)(seconds / 10) * 10;

        return mark > lastSaid ? mark : null;
    }
}
