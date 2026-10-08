namespace Wander.Core.Persistence;

/// <summary>
/// Where the full screen's 1:1 zoom goes in when Z is pressed with the
/// pointer hidden - no mouse to say where (decision 2026-10-08). With the
/// pointer on screen it goes in under it, whatever this says.
/// </summary>
public enum ZoomSpot {
    /// <summary>Where the crisp edges of the frame are thickest (<c>SharpZone</c>).</summary>
    SharpZone,

    /// <summary>
    /// The autofocus area the camera reports in focus. Not every camera
    /// records one, and the one recorded is where the camera focused before
    /// the frame was recomposed - without one, the sharp zone.
    /// </summary>
    AfPoint,
}
