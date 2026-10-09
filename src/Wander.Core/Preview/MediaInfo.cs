using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

public enum MediaTrackKind {
    Video,
    Audio,
    Subtitle,
}


/// <summary>
/// One stream of a media file, as its container describes it. Every field
/// past the kind and the codec is optional: a container says what it says,
/// and a field it does not carry is left at zero or null rather than guessed.
/// </summary>
/// <param name="Codec">Short and universal - "H.264", "AC-3", "SRT" - or the raw tag when the name is not known.</param>
public sealed record MediaTrack(MediaTrackKind Kind, string Codec) {
    /// <summary>Picture size as it plays: rotation applied.</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    public double? FrameRate { get; init; }

    /// <summary>Bits per sample of the picture, when the codec's own header says (10 for most HDR).</summary>
    public int? BitDepth { get; init; }

    /// <summary>"HDR10", "HLG", "Dolby Vision", or null for a standard range picture.</summary>
    public string? Hdr { get; init; }

    public int Channels { get; init; }

    public int SampleRate { get; init; }

    /// <summary>Bits per second, measured from the track's samples when the container lists them.</summary>
    public long? Bitrate { get; init; }

    /// <summary>The container's language tag - ISO 639-2 ("rus") or BCP 47 ("ru"); null when undetermined.</summary>
    public string? Language { get; init; }

    public string? Title { get; init; }

    /// <summary>Subtitles shown whatever the viewer picked - signs and foreign speech.</summary>
    public bool IsForced { get; init; }
}


/// <summary>What a video file's container says about it: the format, the length, the streams.</summary>
/// <param name="Container">"MP4", "MOV", "Matroska", "WebM", "AVI".</param>
public sealed record MediaInfo(string Container, TimeSpan? Duration, IReadOnlyList<MediaTrack> Tracks) {
    /// <summary>The whole file over its length, bits per second.</summary>
    public long? Bitrate { get; init; }

    /// <summary>The first picture stream - what the facts line describes.</summary>
    public MediaTrack? Video => Tracks.FirstOrDefault(t => t.Kind == MediaTrackKind.Video);
}


/// <summary>
/// Reads the description of a video file - container, length, streams with
/// their codecs, sizes, languages - from the container's own headers. No
/// decoding and no system codecs: the answer is the same whether or not
/// Windows can play the file, which is exactly when it is wanted most.
///
/// <para>
/// The container is told by its first bytes, not by the extension: a
/// <c>.mp4</c> that is really Matroska is described as what it is.
/// ISO media (MP4, MOV, 3GP), Matroska (MKV, WebM) and RIFF AVI are read;
/// anything else is null, and the footer says what it said before.
/// </para>
/// </summary>
public static class MediaProbe {
    /// <summary>What the bitrate is measured against: a file this short says nothing sensible.</summary>
    private const double MinSecondsForBitrate = 0.5;


    /// <summary>The description of the file at <paramref name="path"/>, or null.</summary>
    public static MediaInfo? Read(string path) {
        try {
            using var stream = SharedRead.Open(path);

            return Read(stream);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            return null;
        }
    }


    /// <summary>Same, from an open seekable stream - the tests build their files in memory.</summary>
    public static MediaInfo? Read(Stream stream) {
        try {
            var head = new byte[12];
            stream.Position = 0;
            if (stream.Read(head, 0, head.Length) < head.Length) {
                return null;
            }

            MediaInfo? info = null;
            if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) {
                info = MatroskaReader.Read(stream);
            } else if (Bytes.Ascii(head, 0) == "RIFF" && Bytes.Ascii(head, 8) == "AVI ") {
                info = AviReader.Read(stream);
            } else if (IsoMediaReader.LooksLike(head)) {
                info = IsoMediaReader.Read(stream);
            }
            if (info is null) {
                return null;
            }

            return info.Duration is { TotalSeconds: >= MinSecondsForBitrate } length
                ? info with { Bitrate = (long)(stream.Length * 8 / length.TotalSeconds) }
                : info;
        } catch (Exception ex) when (
            ex is IOException or EndOfStreamException or ArgumentException or OverflowException
                or IndexOutOfRangeException or InvalidOperationException) {
            // A damaged header: the file is described as if it had none.
            return null;
        }
    }
}
