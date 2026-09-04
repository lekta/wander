namespace Wander.Core.Operations;

/// <summary>
/// How fast an operation is going and how long it has left, worked out from
/// nothing but the samples it is fed: a time and a bytes-done total, as
/// often as the tracker reports. No clock of its own and no timer - the
/// caller passes the time in, so a test can hand it a whole minute in four
/// calls.
///
/// <para>
/// The average is taken over a short trailing window rather than over the
/// whole run: a copy that started on a fast SSD and is now crawling over
/// USB has to say so, and "since the beginning" would keep promising the
/// old speed for minutes. Three seconds is long enough to ride out the
/// write cache and short enough to notice the volume changing.
/// </para>
/// </summary>
public sealed class TransferRate {
    /// <summary>How far back the average reaches.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Below this the two ends of the window are the same moment and the
    /// division is noise, not a speed.
    /// </summary>
    public static readonly TimeSpan MinSpan = TimeSpan.FromMilliseconds(500);

    private readonly TimeSpan _window;
    private readonly Queue<Sample> _samples = new();


    public TransferRate(TimeSpan? window = null) {
        _window = window ?? Window;
    }


    /// <summary>Bytes a second over the trailing window, or null while there is not enough to say.</summary>
    public double? BytesPerSecond {
        get {
            if (_samples.Count < 2) {
                return null;
            }

            var first = _samples.Peek();
            var last = _samples.Last();
            var span = last.AtUtc - first.AtUtc;
            long moved = last.BytesDone - first.BytesDone;
            if (span < MinSpan || moved <= 0) {
                return null;
            }

            return moved / span.TotalSeconds;
        }
    }


    /// <summary>
    /// One more reading. Samples older than the window are dropped, and a
    /// counter that went backwards (an estimate trued down) starts the
    /// window over rather than reporting a negative speed.
    /// </summary>
    public void Add(DateTime atUtc, long bytesDone) {
        if (_samples.Count > 0 && bytesDone < _samples.Last().BytesDone) {
            _samples.Clear();
        }

        _samples.Enqueue(new Sample(atUtc, bytesDone));
        while (_samples.Count > 2 && atUtc - _samples.Peek().AtUtc > _window) {
            _samples.Dequeue();
        }
    }

    /// <summary>
    /// What is left at the current speed, or null when the speed is not
    /// known yet or nothing is left. Rounded to whole seconds: a remaining
    /// time given to the millisecond is a lie told precisely.
    /// </summary>
    public TimeSpan? Remaining(long bytesRemaining) {
        if (bytesRemaining <= 0 || BytesPerSecond is not { } speed || speed <= 0) {
            return null;
        }

        double seconds = bytesRemaining / speed;

        // A year of "remaining" is a stalled copy, not an estimate.
        return seconds > TimeSpan.FromDays(365).TotalSeconds
            ? null
            : TimeSpan.FromSeconds(Math.Round(seconds));
    }


    private readonly record struct Sample(DateTime AtUtc, long BytesDone);
}
