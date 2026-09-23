namespace Wander.Core.Layout;

/// <summary>
/// Scrolling at the edge (U2, REDESIGN 4.13, decision B20): a drag, or the
/// selection rectangle, held near the top or the bottom of a list scrolls it
/// that way - faster the closer to the edge, fastest at it and past it. The
/// speed grows with the square of the depth into the zone: slow enough to
/// stop on the right row just inside it, fast enough to cross a folder of
/// thousands at the very edge. One rule for the list in every view, both
/// panels and the selection rectangle; the timer that applies it is theirs.
/// </summary>
public static class EdgeScroll {
    /// <summary>How deep the zone at each edge is, in pixels.</summary>
    public const double Zone = 24;

    /// <summary>The top speed, at the edge and past it, in pixels per second.</summary>
    public const double MaxSpeed = 1500;


    /// <summary>
    /// The speed for a cursor <paramref name="distance"/> pixels inside an
    /// edge (negative: past it, outside the surface): nothing beyond the
    /// zone, up to <paramref name="max"/> at the edge.
    /// </summary>
    public static double Velocity(double distance, double zone = Zone, double max = MaxSpeed) {
        if (distance >= zone) {
            return 0;
        }

        double depth = Math.Clamp((zone - distance) / zone, 0, 1);

        return max * depth * depth;
    }

    /// <summary>
    /// The signed speed along an axis for a cursor at <paramref name="position"/>
    /// on a surface <paramref name="length"/> long: towards the start
    /// (negative) near the start, towards the end near the end, none in
    /// between. A surface too short for two zones splits in the middle.
    /// </summary>
    public static double Along(double position, double length, double zone = Zone, double max = MaxSpeed) {
        double reach = Math.Min(zone, length / 2);
        if (reach <= 0) {
            return 0;
        }

        double back = Velocity(position, reach, max);
        double on = Velocity(length - position, reach, max);

        return on - back;
    }

    /// <summary>How far <paramref name="velocity"/> goes in <paramref name="elapsedMs"/>.</summary>
    public static double Step(double velocity, double elapsedMs) {
        return velocity * Math.Max(0, elapsedMs) / 1000;
    }
}
