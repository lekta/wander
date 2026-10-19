namespace Wander.Core.Shell;

/// <summary>
/// A file that reads only forwards - one on a phone or a camera
/// (<see cref="IDeviceContent.OpenRead"/>) - made seekable by keeping what
/// has been read: a seek forward reads on to there, a seek back is answered
/// from what is kept. What a reader asked for is then the head of the file
/// (<see cref="OpenHead"/>): for a RAW's embedded pictures a few megabytes
/// of thirty (<see cref="TempExtraction.CopyHead"/>). Past the limit it
/// reads no more and throws <see cref="IOException"/>, which the RAW reader
/// takes for "no picture". The source stays the caller's.
/// </summary>
public sealed class HeadStream : Stream {
    /// <summary>Read on in steps of this: what a device reads best in (WPD, stand Y4a).</summary>
    private const int Step = 256 * 1024;

    private readonly Stream _source;
    private readonly long _length;
    private readonly long _limit;
    private readonly CancellationToken _ct;
    private byte[] _kept = Array.Empty<byte>();
    private int _count;
    private long _position;


    /// <param name="source">The file, read from its first byte on.</param>
    /// <param name="length">The file's length - the listing's.</param>
    /// <param name="limit">The most that is read off the source.</param>
    /// <param name="ct">Asked between steps.</param>
    public HeadStream(Stream source, long length, long limit, CancellationToken ct = default) {
        _source = source;
        _length = length;
        _limit = Math.Min(limit, Array.MaxLength);
        _ct = ct;
    }


    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>How much of the file has come over.</summary>
    public int HeadLength => _count;


    /// <summary>What has come over, as a stream of its own.</summary>
    public Stream OpenHead() {
        return new MemoryStream(_kept, 0, _count, writable: false);
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (count == 0 || _position >= _length) {
            return 0;
        }

        long end = Math.Min(_position + count, _length);
        Fill(end);
        int got = (int)Math.Max(0, Math.Min(end, _count) - _position);
        Buffer.BlockCopy(_kept, (int)_position, buffer, offset, got);
        _position += got;

        return got;
    }

    public override long Seek(long offset, SeekOrigin origin) {
        Position = origin switch {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => _length + offset,
        };

        return _position;
    }

    public override void Flush() {
    }

    public override void SetLength(long value) {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotSupportedException();
    }


    /// <summary>Reads on until <paramref name="upTo"/> is kept, a step at a time; the file ending early ends it.</summary>
    private void Fill(long upTo) {
        if (upTo <= _count) {
            return;
        }
        if (upTo > _limit) {
            throw new IOException($"Past the head: byte {upTo} of a {_limit}-byte limit");
        }

        long last = Math.Min(_length, _limit);
        long target = Math.Min(last, (upTo + Step - 1) / Step * Step);
        if (_kept.Length < target) {
            Array.Resize(ref _kept, (int)Math.Min(last, Math.Max(target, 2L * _kept.Length)));
        }
        while (_count < target) {
            _ct.ThrowIfCancellationRequested();
            int got = _source.Read(_kept, _count, (int)(target - _count));
            if (got == 0) {
                break;
            }
            _count += got;
        }
    }
}
