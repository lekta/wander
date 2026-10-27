namespace Wander.Core.Preview;

/// <summary>
/// The shape of a sound file (<see cref="Waveform"/>), decoded by whatever
/// the platform has and remembered per file as it was when read.
/// </summary>
public interface IWaveformSource {
    /// <summary>
    /// The shape of <paramref name="path"/>; null when the file cannot be
    /// decoded or runs longer than <paramref name="limit"/>. Slow - decodes
    /// the whole track; call off the UI thread.
    /// </summary>
    Waveform? Read(string path, TimeSpan? limit, CancellationToken cancel);

    /// <summary>
    /// What <see cref="Read"/> would answer, when it has already been
    /// worked out for the file as it is now - null included. False when it
    /// has not.
    /// </summary>
    bool TryGetCached(string path, TimeSpan? limit, out Waveform? shape);
}
