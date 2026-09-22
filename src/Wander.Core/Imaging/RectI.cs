namespace Wander.Core.Imaging;

/// <summary>A rectangle of whole pixels: left, top, width, height.</summary>
public readonly record struct RectI(int X, int Y, int Width, int Height);
