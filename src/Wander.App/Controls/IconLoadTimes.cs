using System.Diagnostics;

namespace Wander.App.Controls;

/// <summary>
/// The moments of one icon load, as stopwatch timestamps - asked for,
/// started (after the layout hop that says whether it is on screen), let
/// through the gate, done (picture made and decoded) and set on the screen.
/// <see cref="FirstScreenWatch"/> writes which wait holds a slow first
/// screen - the control line that found the starved landing (2026-11-09:
/// pictures done in milliseconds, on screen a second later).
/// </summary>
internal sealed class IconLoadTimes {
    public long Asked { get; } = Stopwatch.GetTimestamp();

    public long Started { get; set; }

    public long Admitted { get; set; }

    public long Done { get; set; }

    public long Applied { get; set; }

    /// <summary>All five moments taken: the picture went the whole way.</summary>
    public bool IsComplete => Started > 0 && Admitted > 0 && Done > 0 && Applied > 0;

    /// <summary>From being asked for to starting: the layout hop.</summary>
    public double LayoutMs => Ms(Asked, Started);

    /// <summary>Waiting for one of the gate's slots.</summary>
    public double GateMs => Ms(Started, Admitted);

    /// <summary>Making the picture and decoding it.</summary>
    public double WorkMs => Ms(Admitted, Done);

    /// <summary>From done to on screen: the dispatcher's queue.</summary>
    public double ApplyMs => Ms(Done, Applied);


    private static double Ms(long from, long to) {
        return Math.Max(0, to - from) * 1000.0 / Stopwatch.Frequency;
    }
}
