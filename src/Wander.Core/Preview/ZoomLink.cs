namespace Wander.Core.Preview;

/// <summary>
/// Two pictures under one held-button zoom (PLAN Q5): the one the mouse is
/// on leads, the other shows the same place of its own picture. With the
/// right button held as well the other stands still and only the leader
/// moves - two frames shot a moment apart are rarely framed alike, and this
/// is how a detail of one is brought over the same detail of the other
/// (2026-09-24). Let go of it, and the two move together again with the
/// shift kept, for as long as the pair lasts (<see cref="Reset"/>).
///
/// <para>
/// Places are shares of each picture's area, 0..1 each way, as the panes
/// report them. A shifted place can fall past an edge; clamping it is the
/// pane's business, the shift stays as it was set.
/// </para>
/// </summary>
public sealed class ZoomLink {
    // The second picture's place less the first's.
    private double _shiftX;
    private double _shiftY;

    // Where each was put last; null before the first move of a zoom.
    private (double X, double Y)? _first;
    private (double X, double Y)? _second;


    /// <summary>
    /// The leader's zoom moved to (<paramref name="x"/>, <paramref name="y"/>).
    /// Answers where the other one's goes, or null when it stays where it is.
    /// </summary>
    /// <param name="fromFirst">The first of the two leads.</param>
    /// <param name="x">The leader's place across, as a share of its picture's area.</param>
    /// <param name="y">Same, down.</param>
    /// <param name="alone">
    /// The right button is held too: the leader moves, the other does not.
    /// Not on the first move of a zoom - the other has to be put somewhere
    /// before it can stand still.
    /// </param>
    public (double X, double Y)? Lead(bool fromFirst, double x, double y, bool alone) {
        var other = fromFirst ? _second : _first;
        if (alone && other is { } still) {
            // The shift is whatever lies between the two now.
            (_shiftX, _shiftY) = fromFirst ? (still.X - x, still.Y - y) : (x - still.X, y - still.Y);
            Remember(fromFirst, (x, y), still);

            return null;
        }

        var follows = fromFirst ? (x + _shiftX, y + _shiftY) : (x - _shiftX, y - _shiftY);
        Remember(fromFirst, (x, y), follows);

        return follows;
    }

    /// <summary>The zoom ended on both pictures. The next one starts with the shift as it is.</summary>
    public void End() {
        _first = null;
        _second = null;
    }

    /// <summary>Another pair: lined up as they come again.</summary>
    public void Reset() {
        End();
        _shiftX = 0;
        _shiftY = 0;
    }


    private void Remember(bool fromFirst, (double X, double Y) leader, (double X, double Y) other) {
        _first = fromFirst ? leader : other;
        _second = fromFirst ? other : leader;
    }
}
