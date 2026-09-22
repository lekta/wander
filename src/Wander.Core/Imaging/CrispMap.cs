namespace Wander.Core.Imaging;

/// <summary>
/// How crisp each point of a frame is (<see cref="Sharpness.Measure"/>),
/// one byte per point: crispness times <see cref="Sharpness.Unit"/>, zero
/// where there was no edge to judge.
///
/// <para>
/// The map can be coarser than the frame - one point per <c>step</c> pixels
/// each way - which is what the gallery's thumbnails use: the measurement
/// still looks at real neighbouring pixels, only fewer of them are asked
/// about, and a mark a hundred pixels wide on the thumbnail does not care.
/// </para>
/// </summary>
public sealed record CrispMap(byte[] Values, int Width, int Height);
