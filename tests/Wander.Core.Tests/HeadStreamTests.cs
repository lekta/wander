using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Shell;
using Wander.Core.Tests.Fakes;
using static Wander.Core.Tests.RawPreviewExtractorTests;

namespace Wander.Core.Tests;

public class HeadStreamTests {
    private const string Shot = @"device:Canon EOS R8\SD1\DCIM\100CANON\IMG_0001.CR3";

    private const long Limit = 32L * 1024 * 1024;


    /// <summary>
    /// A CR3 the way a camera lays it out: the movie box pointing at the
    /// full-size JPEG, then the media data - that JPEG first, the sensor's
    /// data after it.
    /// </summary>
    private static byte[] Cr3(byte[] jpeg, int sensorBytes) {
        byte[] ftyp = Box("ftyp", new byte[16]);
        int moovLength = Moov(0, jpeg.Length, wide: true).Length;
        byte[] moov = Moov(ftyp.Length + moovLength + 8, jpeg.Length, wide: true);
        byte[] mdat = Box("mdat", jpeg, new byte[sensorBytes]);

        return ftyp.Concat(moov).Concat(mdat).ToArray();
    }


    [Fact]
    public void TheHead_IsWhatTheEmbeddedPictureNeeds_AndAPictureOnItsOwn() {
        // Over the cable a CR3 is read from its first byte on: the head is
        // the movie box and the JPEG, not the 4 MB of sensor data after.
        byte[] jpeg = BaselineJpeg(padding: 1000);
        byte[] file = Cr3(jpeg, sensorBytes: 4 * 1024 * 1024);
        var source = new ForwardOnly(file);
        var head = new HeadStream(source, file.Length, Limit);

        Assert.Equal(jpeg, RawPreviewExtractor.Extract(head, fullSize: true));
        Assert.InRange(head.HeadLength, jpeg.Length, 512 * 1024);
        Assert.Equal(head.HeadLength, source.Given);

        // What a head file holds is enough for the preview pane alone.
        using var kept = head.OpenHead();
        Assert.Equal(jpeg, RawPreviewExtractor.Extract(kept, fullSize: true));
    }

    [Fact]
    public void PastTheLimit_ThereIsNoPicture_AndNothingMoreIsRead() {
        // The JPEG lies past what a glance may read: the RAW reader takes
        // the stop for "no picture", and the caller goes the old way.
        byte[] file = Cr3(BaselineJpeg(padding: 600 * 1024), sensorBytes: 1024);
        var source = new ForwardOnly(file);
        var head = new HeadStream(source, file.Length, limit: 300 * 1024);

        Assert.Null(RawPreviewExtractor.Extract(head, fullSize: true));
        Assert.True(source.Given <= 300 * 1024);
    }

    [Fact]
    public void ASeekBack_IsAnsweredFromWhatWasRead() {
        byte[] file = Enumerable.Range(0, 600 * 1024).Select(i => (byte)(i * 7)).ToArray();
        var head = new HeadStream(new ForwardOnly(file), file.Length, Limit);
        var far = new byte[16];
        var near = new byte[16];

        head.Position = 400 * 1024;
        Assert.Equal(16, head.Read(far, 0, far.Length));
        head.Seek(100, SeekOrigin.Begin);
        Assert.Equal(16, head.Read(near, 0, near.Length));

        Assert.Equal(file.AsSpan(400 * 1024, 16).ToArray(), far);
        Assert.Equal(file.AsSpan(100, 16).ToArray(), near);
    }

    [Fact]
    public void CopyHead_WritesTheHead_WhereOnlyThePreviewLooks_AndReusesIt() {
        byte[] jpeg = BaselineJpeg(padding: 1000);
        byte[] file = Cr3(jpeg, sensorBytes: 2 * 1024 * 1024);
        var device = new FakeDeviceContent();
        device.AddFile(Shot, file, DateTime.UtcNow);
        var fs = new FakeFileSystem();
        var entry = device.GetEntry(Shot)!;

        string? first = TempExtraction.CopyHead(device, fs, NullLogger.Instance, entry, Limit, CancellationToken.None);
        string? again = TempExtraction.CopyHead(device, fs, NullLogger.Instance, entry, Limit, CancellationToken.None);

        Assert.Equal(TempExtraction.HeadPathFor(Shot, file.Length), first);
        Assert.NotEqual(TempExtraction.CopyPathFor(Shot), first);
        Assert.Equal("IMG_0001.CR3", Path.GetFileName(first));
        Assert.Equal(first, again);
        Assert.Single(device.Opened);
        Assert.True(fs.Files[first!].Length < file.Length);
        Assert.Equal(jpeg, RawPreviewExtractor.Extract(new MemoryStream(fs.Files[first!]), fullSize: true));
    }

    [Fact]
    public void CopyHead_OfAFileWithNoPicture_WritesNothing() {
        var device = new FakeDeviceContent();
        device.AddFile(Shot, new byte[64 * 1024], DateTime.UtcNow);
        var fs = new FakeFileSystem();

        string? head = TempExtraction.CopyHead(device, fs, NullLogger.Instance, device.GetEntry(Shot)!, Limit, CancellationToken.None);

        Assert.Null(head);
        Assert.Empty(fs.Files);
    }


    /// <summary>A file the way a device gives it: forwards only, and how much of it went over.</summary>
    private sealed class ForwardOnly : Stream {
        private readonly byte[] _bytes;

        public ForwardOnly(byte[] bytes) {
            _bytes = bytes;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _bytes.Length;

        public override long Position {
            get => Given;
            set => throw new NotSupportedException();
        }

        public int Given { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) {
            int got = Math.Min(count, _bytes.Length - Given);
            Buffer.BlockCopy(_bytes, Given, buffer, offset, got);
            Given += got;

            return got;
        }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        public override void Flush() {
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }
    }
}
