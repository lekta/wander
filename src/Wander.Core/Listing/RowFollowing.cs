namespace Wander.Core.Listing;

/// <summary>Where the list's main row stood on screen as rows began to land on it.</summary>
/// <param name="Path">The row, by its path then.</param>
/// <param name="Top">How far from the top of the view - rows in the table, pixels in the tiles.</param>
/// <param name="AtStart">The list was scrolled to its very start.</param>
public readonly record struct RowStand(string Path, double Top, bool AtStart);


/// <summary>What the view does about the main row once the rows have landed.</summary>
public enum RowFollow {
    /// <summary>Nothing moves.</summary>
    None,

    /// <summary>Brought into view, moving as little as possible; in view already, nothing moves.</summary>
    Reveal,

    /// <summary>Put back where it stood.</summary>
    Hold,
}


/// <summary>
/// The selected file does not leave the screen on its own (decision
/// 2026-09-28): a main row that stood on screen when rows began to land is
/// there after they have - at the same place, whatever came, went or moved
/// around it: the watcher, F5, an operation, a filter, another order, a
/// rename made elsewhere, search results coming and going. A row the user
/// had scrolled away from is left where it is, unless the landing is one
/// that shows its row (<see cref="ListLanding.Scroll"/>).
///
/// <para>
/// One exception, a browser's: a list standing at its very start stays
/// there while the row still fits on screen - rows arriving above it are
/// what the user is looking at, and holding the row would send them off
/// screen. It gives way only to a change the user made to the rows
/// themselves.
/// </para>
/// </summary>
public static class RowFollowing {
    /// <param name="stood">Where the main row stood before the landing; null when it was not on screen, or nothing was selected.</param>
    /// <param name="held">The row whose place the main row keeps (<see cref="ListLanding.Held"/>).</param>
    /// <param name="reveal">The landing shows its main row (<see cref="ListLanding.Scroll"/>).</param>
    public static RowFollow Decide(RowStand? stood, string? held, bool reveal) {
        if (stood is not { } was || held is null
            || !string.Equals(was.Path, held, StringComparison.OrdinalIgnoreCase)) {
            return reveal ? RowFollow.Reveal : RowFollow.None;
        }

        return was.AtStart && !reveal ? RowFollow.Reveal : RowFollow.Hold;
    }
}
