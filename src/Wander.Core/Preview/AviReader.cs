namespace Wander.Core.Preview;

/// <summary>
/// RIFF AVI: chunks of a four-character code and a little-endian size,
/// lists of them, padded to even lengths. The header list (<c>hdrl</c>)
/// opens the file and is everything a description needs: the main header
/// (<c>avih</c>), a stream list (<c>strl</c>) per stream - its header
/// (<c>strh</c>, rate and length), its format (<c>strf</c>, a
/// <c>BITMAPINFOHEADER</c> or a <c>WAVEFORMATEX</c>) and its name - and
/// the OpenDML frame count of files past a gigabyte.
/// </summary>
internal static class AviReader {
    /// <summary>A header list past this is not one worth reading for a footer.</summary>
    private const int MaxHeaderList = 16 * 1024 * 1024;


    public static MediaInfo? Read(Stream stream) {
        var head = new byte[12];
        long position = 12;
        long end = stream.Length;
        while (position + 12 <= end) {
            stream.Position = position;
            stream.ReadExactly(head, 0, 12);
            string id = Bytes.Ascii(head, 0);
            long size = Bytes.U32Le(head, 4);
            if (id == "LIST" && Bytes.Ascii(head, 8) == "hdrl") {
                if (size > MaxHeaderList || position + 8 + size > end) {
                    return null;
                }
                var list = new byte[size - 4];
                stream.ReadExactly(list);

                return Describe(list);
            }
            position += 8 + size + (size & 1);
        }

        return null;
    }


    private static MediaInfo Describe(byte[] d) {
        long microsPerFrame = 0, totalFrames = 0;
        long? openDmlFrames = null;
        TimeSpan? videoLength = null;
        var tracks = new List<MediaTrack>();
        foreach (var chunk in Chunks(d, 0, d.Length)) {
            if (chunk.Id == "avih" && chunk.Length >= 40) {
                microsPerFrame = Bytes.U32Le(d, chunk.Start);
                totalFrames = Bytes.U32Le(d, chunk.Start + 16);
            } else if (chunk.Id == "strl") {
                if (StreamList(d, chunk) is { } stream) {
                    tracks.Add(stream.Track);
                    if (stream.Track.Kind == MediaTrackKind.Video) {
                        videoLength ??= stream.Length;
                    }
                }
            } else if (chunk.Id == "odml") {
                foreach (var dmlh in Chunks(d, chunk.Start, chunk.End)) {
                    if (dmlh.Id == "dmlh" && dmlh.Length >= 4) {
                        openDmlFrames = Bytes.U32Le(d, dmlh.Start);
                    }
                }
            }
        }

        long frames = openDmlFrames ?? totalFrames;
        TimeSpan? duration = videoLength;
        if (frames > 0 && microsPerFrame > 0 && (duration is null || openDmlFrames is not null)) {
            duration = TimeSpan.FromSeconds(frames * (microsPerFrame / 1e6));
        }

        return new MediaInfo("AVI", duration, tracks);
    }


    private static (MediaTrack Track, TimeSpan? Length)? StreamList(byte[] d, Chunk strl) {
        string type = "";
        long scale = 0, rate = 0, length = 0;
        Chunk? format = null;
        string? name = null;
        foreach (var chunk in Chunks(d, strl.Start, strl.End)) {
            switch (chunk.Id) {
                case "strh" when chunk.Length >= 36:
                    type = Bytes.Ascii(d, chunk.Start);
                    scale = Bytes.U32Le(d, chunk.Start + 20);
                    rate = Bytes.U32Le(d, chunk.Start + 24);
                    length = Bytes.U32Le(d, chunk.Start + 32);
                    break;

                case "strf":
                    format = chunk;
                    break;

                case "strn" when chunk.Length > 0:
                    name = System.Text.Encoding.UTF8.GetString(d, chunk.Start, chunk.Length).TrimEnd('\0').Trim();
                    break;
            }
        }

        TimeSpan? span = scale > 0 && rate > 0 && length > 0 ? TimeSpan.FromSeconds(length * (double)scale / rate) : null;
        string? title = string.IsNullOrEmpty(name) ? null : name;
        switch (type) {
            case "vids" when format is { Length: >= 20 } f:
                return (new MediaTrack(MediaTrackKind.Video, MediaCodecs.VideoFourcc(Bytes.Ascii(d, f.Start + 16))) {
                    Width = Math.Abs((int)Bytes.U32Le(d, f.Start + 4)),
                    Height = Math.Abs((int)Bytes.U32Le(d, f.Start + 8)),
                    FrameRate = scale > 0 && rate > 0 ? (double)rate / scale : null,
                    Title = title,
                }, span);

            case "auds" when format is { Length: >= 16 } f: {
                    long bytesPerSecond = Bytes.U32Le(d, f.Start + 8);

                    return (new MediaTrack(MediaTrackKind.Audio, MediaCodecs.WaveFormat(d, f.Start, f.End)) {
                        Channels = Bytes.U16Le(d, f.Start + 2),
                        SampleRate = (int)Bytes.U32Le(d, f.Start + 4),
                        Bitrate = bytesPerSecond > 0 ? bytesPerSecond * 8 : null,
                        Title = title,
                    }, span);
                }

            case "txts":
                return (new MediaTrack(MediaTrackKind.Subtitle, "Text") { Title = title }, span);

            default:
                return null;
        }
    }


    /// <summary>
    /// The chunks between <paramref name="start"/> and <paramref name="end"/>;
    /// a list comes back as its type (<c>strl</c>, <c>odml</c>) with the
    /// payload past that type.
    /// </summary>
    private static IEnumerable<Chunk> Chunks(byte[] d, int start, int end) {
        int p = start;
        while (p + 8 <= end) {
            string id = Bytes.Ascii(d, p);
            long size = Bytes.U32Le(d, p + 4);
            if (size > end - p - 8) {
                yield break;
            }

            int body = p + 8;
            int bodyEnd = (int)(body + size);
            if (id == "LIST" && size >= 4) {
                yield return new Chunk(Bytes.Ascii(d, body), body + 4, bodyEnd);
            } else {
                yield return new Chunk(id, body, bodyEnd);
            }
            p = bodyEnd + (int)(size & 1);
        }
    }


    private readonly record struct Chunk(string Id, int Start, int End) {
        public int Length => End - Start;
    }
}
