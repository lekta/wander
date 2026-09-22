namespace Wander.Core.Imaging;

/// <summary>
/// One autofocus area the camera recorded, in shares of the frame: centre
/// <see cref="X"/>, <see cref="Y"/> from the top left, size
/// <see cref="W"/> by <see cref="H"/>, all 0..1 - so it lies over the
/// picture at whatever size the picture is drawn.
/// </summary>
/// <param name="InFocus">The camera reports this area as the one that achieved focus.</param>
public sealed record AfPoint(double X, double Y, double W, double H, bool InFocus);
