using System.Diagnostics;
using System.Runtime.InteropServices;
using Wander.Core.Logging;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// A COM <c>IStream</c> read forwards as a .NET <see cref="Stream"/> - a file
/// on a phone or a camera, the way Wander's copy loop and its decoders read
/// any other. The length is given by whoever opened it: a device states a
/// file's size in its listing, and its stream may not say (stand Y4a).
/// Disposing releases the COM stream: a stream dropped half-way is how a
/// copy is stopped inside a file, and the next one opens at once.
///
/// <para>
/// A device's stream left before its end costs its rest: the R8's driver
/// reads the file through before letting go - ~420 ms of the 500 a CR3
/// preview takes off the camera, after its head was read in 80.
/// <c>IPortableDeviceDataStream.Cancel</c> first did not change that
/// (2026-10-22: 416-435 ms over 15 files); letting go in the background is
/// PLAN, version 0.7. One line in the log says what each letting go cost.
/// </para>
/// </summary>
internal sealed class ComReadStream : Stream {
    private readonly long _length;

    private ComTypes.IStream? _stream;
    private IntPtr _read;
    private long _position;


    public ComReadStream(ComTypes.IStream stream, long length) {
        _stream = stream;
        _length = length;
        _read = Marshal.AllocHGlobal(sizeof(int));
    }


    public override bool CanRead => _stream is not null;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position {
        get => _position;
        set => throw new NotSupportedException();
    }


    public override int Read(byte[] buffer, int offset, int count) {
        var stream = _stream ?? throw new ObjectDisposedException(nameof(ComReadStream));
        if (count == 0) {
            return 0;
        }

        // IStream.Read fills from the start of the array it is given.
        byte[] target = offset == 0 ? buffer : new byte[count];
        stream.Read(target, count, _read);
        int got = Marshal.ReadInt32(_read);
        if (offset != 0 && got > 0) {
            Buffer.BlockCopy(target, 0, buffer, offset, got);
        }
        _position += got;

        return got;
    }

    public override void Flush() {
    }

    public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException();
    }

    public override void SetLength(long value) {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotSupportedException();
    }


    protected override void Dispose(bool disposing) {
        if (_stream is not null) {
            LetGo(_stream);
            _stream = null;
        }
        if (_read != IntPtr.Zero) {
            Marshal.FreeHGlobal(_read);
            _read = IntPtr.Zero;
        }

        base.Dispose(disposing);
    }


    private void LetGo(ComTypes.IStream stream) {
        bool early = _length > 0 && _position < _length;
        var clock = Stopwatch.StartNew();
        Marshal.ReleaseComObject(stream);
        if (early) {
            Log.Info($"Device stream let go at {_position} of {_length} bytes in {clock.ElapsedMilliseconds} ms");
        }
    }
}
