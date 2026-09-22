namespace Wander.Core.Imaging;

/// <summary>
/// A picture as plain bytes: four per pixel in the order blue, green, red,
/// alpha, rows <see cref="Stride"/> bytes apart. What the review helpers
/// work on - the view turns its bitmap into one of these and the answers
/// back into something to draw, so everything in between is arithmetic a
/// test can reach.
/// </summary>
public sealed record BgraImage(byte[] Pixels, int Width, int Height, int Stride);
