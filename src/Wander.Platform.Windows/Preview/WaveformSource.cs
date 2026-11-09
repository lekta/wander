using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Preview;

/// <summary>
/// The shape of a sound file: a plain WAV read by <see cref="WavShape"/>,
/// everything else - and WAVs it does not know - by
/// <see cref="MediaFoundationShape"/>.
///
/// <para>
/// Remembered in memory per file as it was when read (<see cref="FileStamp"/>),
/// the way <c>SharpnessProbe</c> keeps its scores: the details column, the
/// preview pane and a folder walked back into ask again, and a shape is a
/// kilobyte. The tiles keep theirs baked into the thumbnail, so nothing
/// here outlives the session.
/// </para>
/// </summary>
public sealed class WaveformSource : IWaveformSource {
    /// <summary>How many answers are kept. Past this the cache starts over.</summary>
    private const int CacheLimit = 8000;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Answer> _answers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tiles' quick shapes (<see cref="Sketch"/>), apart from the whole ones: the pane must never be handed a sketch.</summary>
    private readonly Dictionary<string, Answer> _sketches = new(StringComparer.OrdinalIgnoreCase);


    public Waveform? Read(string path, TimeSpan? limit, CancellationToken cancel) {
        if (!TryStamp(path, out var stamp)) {
            return null;
        }
        if (Cached(_answers, path, stamp, limit, out var kept)) {
            return kept;
        }

        Answer answer;
        using (PerfLog.Measure("bg.waveform")) {
            answer = Decode(path, limit, cancel);
        }
        Keep(_answers, path, answer with { Stamp = stamp });

        return answer.Shape;
    }

    public Waveform? Sketch(string path, TimeSpan? limit, CancellationToken cancel) {
        // A plain WAV is read whole as quickly as it would be sampled:
        // nothing to decode.
        if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)) {
            return Read(path, limit, cancel);
        }
        if (!TryStamp(path, out var stamp)) {
            return null;
        }
        // The whole shape, once the pane or the column has decoded it, is
        // the better one and costs nothing.
        if (Cached(_answers, path, stamp, limit, out var whole) || Cached(_sketches, path, stamp, limit, out whole)) {
            return whole;
        }

        Waveform? shape;
        using (PerfLog.Measure("bg.waveform-sketch")) {
            shape = MediaFoundationShape.ReadSketch(path, limit, cancel);
        }
        Keep(_sketches, path, new Answer(stamp, shape, shape is null ? limit : null));

        return shape;
    }

    public bool TryGetCached(string path, TimeSpan? limit, out Waveform? shape) {
        shape = null;

        return TryStamp(path, out var stamp) && Cached(_answers, path, stamp, limit, out shape);
    }


    private void Keep(Dictionary<string, Answer> answers, string path, Answer answer) {
        lock (_lock) {
            if (answers.Count >= CacheLimit) {
                answers.Clear();
            }
            answers[path] = answer;
        }
    }

    private bool Cached(Dictionary<string, Answer> answers, string path, FileStamp stamp, TimeSpan? limit, out Waveform? shape) {
        shape = null;
        Answer kept;
        lock (_lock) {
            if (!answers.TryGetValue(path, out kept!) || kept.Stamp != stamp) {
                return false;
            }
        }

        if (kept.Shape is { } known) {
            // Read without a limit, asked with one: the answer is still known.
            shape = limit is { } most && known.Duration > most ? null : known;

            return true;
        }

        // Too long for an earlier limit says nothing about a longer one.
        if (kept.TooLongFor is { } earlier) {
            return limit is { } asked && asked <= earlier;
        }

        return true;
    }

    private static Answer Decode(string path, TimeSpan? limit, CancellationToken cancel) {
        try {
            if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)) {
                using var stream = SharedRead.Open(path, 64 * 1024, FileOptions.SequentialScan);
                if (WavShape.ReadFormat(stream) is { } format) {
                    if (limit is { } most && format.Duration > most) {
                        return new Answer(default, null, most);
                    }

                    return new Answer(default, WavShape.Read(stream, format, cancel), null);
                }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return new Answer(default, null, null);
        }

        var shape = MediaFoundationShape.Read(path, limit, cancel);

        // Media Foundation answers null both for "cannot" and for "too
        // long"; with a limit, the null is kept for that limit only, so the
        // preview pane, which asks without one, still decodes it.
        return new Answer(default, shape, shape is null ? limit : null);
    }

    private static bool TryStamp(string path, out FileStamp stamp) {
        try {
            var file = new FileInfo(path);
            stamp = FileStamp.Of(file.LastWriteTimeUtc, file.Length);

            return file.Exists;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
            stamp = default;

            return false;
        }
    }


    /// <summary>What a file answered: its shape, or null - too long for <see cref="TooLongFor"/>, or unreadable.</summary>
    private sealed record Answer(FileStamp Stamp, Waveform? Shape, TimeSpan? TooLongFor);
}
