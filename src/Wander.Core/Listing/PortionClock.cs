namespace Wander.Core.Listing;

/// <summary>
/// When a listing that is still being read gives the list what it has so
/// far (PLAN AD2): the Recycle Bin on a cold disk spends its seconds spread
/// over the rows - 6.4 s for 1469 rows, the first after 12 ms (session log
/// of 2026-09-21) - and a list that fills in beats one that stays empty to
/// the end. A listing quicker than <see cref="FirstMs"/> gives nothing
/// early and lands once, as before; a slow one gives a portion then and
/// every <see cref="EveryMs"/> after, each a landing of its own.
/// </summary>
public sealed class PortionClock {
    /// <summary>Quicker than this, a listing lands whole: a portion would only be a second landing.</summary>
    public const int FirstMs = 300;

    /// <summary>Between portions: each one re-sorts what is there and lays it down on the list.</summary>
    public const int EveryMs = 1000;

    private long _next = FirstMs;


    /// <summary>Whether a portion is due at <paramref name="elapsedMs"/> since the listing began; true once per interval.</summary>
    public bool Due(long elapsedMs) {
        if (elapsedMs < _next) {
            return false;
        }

        _next = elapsedMs + EveryMs;

        return true;
    }
}
