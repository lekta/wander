namespace Wander.Core.Panels;

/// <summary>When a cursor move in a panel opens the folder it lands on.</summary>
public enum ThrottleOutcome {
    /// <summary>It does not: the arrow keys only move the cursor (the default).</summary>
    Never,

    /// <summary>Right away.</summary>
    Now,

    /// <summary>At <see cref="ThrottleDecision.AtMs"/>, if the cursor has rested there by then.</summary>
    At,
}


/// <summary>The answer of <see cref="TreeNavThrottle.Decide"/>.</summary>
public readonly record struct ThrottleDecision(ThrottleOutcome Outcome, long AtMs) {
    public static readonly ThrottleDecision Never = new(ThrottleOutcome.Never, 0);
    public static readonly ThrottleDecision Now = new(ThrottleOutcome.Now, 0);
}


/// <summary>
/// With "arrows open folders" on, a held arrow key moves the cursor several
/// rows a second, and each row opened is a full navigation - listing,
/// layout, thumbnails; run every one and the window falls seconds behind the
/// key (measured: ui.stall 3.6-4.9 s). A lone press opens its row at once;
/// a press on the heels of a navigation waits until the cursor rests, so a
/// burst costs one listing - of the folder the user stopped on. The clock is
/// the caller's (like <c>TypeAheadController</c>): the decision is here, the
/// timer that waits for it is the application's.
/// </summary>
public static class TreeNavThrottle {
    /// <summary>A press this soon after a navigation is part of a burst.</summary>
    public const int BurstMs = 250;

    /// <summary>How long the cursor has to rest before a burst opens its row.</summary>
    public const int SettleMs = 90;


    /// <param name="arrowsOpen">The setting: the arrow keys open the folder under the cursor.</param>
    /// <param name="nowMs">The moment of the press.</param>
    /// <param name="lastNavigationMs">When a panel last opened a folder, or null.</param>
    public static ThrottleDecision Decide(bool arrowsOpen, long nowMs, long? lastNavigationMs) {
        if (!arrowsOpen) {
            return ThrottleDecision.Never;
        }

        return lastNavigationMs is { } last && nowMs - last < BurstMs
            ? new ThrottleDecision(ThrottleOutcome.At, nowMs + SettleMs)
            : ThrottleDecision.Now;
    }
}
