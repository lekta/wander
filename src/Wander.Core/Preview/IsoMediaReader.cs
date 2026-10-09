namespace Wander.Core.Preview;

/// <summary>
/// ISO base media - MP4, MOV, 3GP: a tree of boxes, each a length and a
/// four-character type. Everything a description needs is in <c>moov</c>,
/// which is read whole (it is the index, megabytes at most, wherever in
/// the file it sits); the media data around it is stepped over by length.
///
/// <para>
/// Per track (<c>trak</c>): the kind from the handler (<c>hdlr</c>), size
/// and rotation from <c>tkhd</c>, language and timescale from <c>mdhd</c>,
/// the codec from the first sample description (<c>stsd</c>) and its
/// configuration boxes, frame rate from the sample durations (<c>stts</c>)
/// and bitrate from the sample sizes (<c>stsz</c>) - measured, not the
/// encoder's promise.
/// </para>
/// </summary>
internal static class IsoMediaReader {
    /// <summary>A movie index past this is not a file worth describing in a footer.</summary>
    private const long MaxMovieBox = 64L * 1024 * 1024;

    /// <summary>ISO 639-2 for "undetermined" - said by saying nothing.</summary>
    private const string Undetermined = "und";

    /// <summary>The 16.16 fixed-point one of a transformation matrix.</summary>
    private const int FixedOne = 0x10000;


    /// <summary>Whether the first bytes start a box this reader knows at the top of a file.</summary>
    public static bool LooksLike(byte[] head) {
        return Bytes.Ascii(head, 4) is "ftyp" or "moov" or "mdat" or "free" or "skip" or "wide" or "pnot";
    }


    public static MediaInfo? Read(Stream stream) {
        string? brand = null;
        byte[]? movie = null;
        long position = 0;
        long length = stream.Length;
        var header = new byte[16];
        while (position + 8 <= length) {
            stream.Position = position;
            stream.ReadExactly(header, 0, 8);
            long size = Bytes.U32(header, 0);
            string type = Bytes.Ascii(header, 4);
            int headerSize = 8;
            if (size == 1) {
                stream.ReadExactly(header, 8, 8);
                size = (long)Bytes.U64(header, 8);
                headerSize = 16;
            } else if (size == 0) {
                size = length - position;
            }
            if (size < headerSize) {
                break;
            }

            if (type == "ftyp" && size >= headerSize + 4) {
                stream.ReadExactly(header, 0, 4);
                brand = Bytes.Ascii(header, 0);
            } else if (type == "moov") {
                long payload = size - headerSize;
                if (payload > MaxMovieBox || position + size > length) {
                    return null;
                }
                movie = new byte[payload];
                stream.ReadExactly(movie);

                break;
            }
            position += size;
        }
        if (movie is null) {
            return null;
        }

        return Describe(movie, brand);
    }


    private static MediaInfo Describe(byte[] movie, string? brand) {
        long timescale = 0;
        long duration = 0;
        var tracks = new List<MediaTrack>();
        foreach (var box in Boxes(movie, 0, movie.Length)) {
            switch (box.Type) {
                case "mvhd":
                    (timescale, duration) = MediaHeader(movie, box);
                    break;

                case "trak":
                    if (Track(movie, box) is { } track) {
                        tracks.Add(track);
                    }
                    break;

                // A fragmented file says its length here, the movie
                // header's own being zero.
                case "mvex" when duration == 0:
                    if (Child(movie, box, "mehd") is { } mehd) {
                        duration = movie[mehd.Start] == 1 ? (long)Bytes.U64(movie, mehd.Start + 4) : Bytes.U32(movie, mehd.Start + 4);
                    }
                    break;
            }
        }

        string container = brand switch {
            null or "qt  " => "MOV",
            _ when brand.StartsWith("3g", StringComparison.Ordinal) => "3GP",
            _ => "MP4",
        };
        TimeSpan? length = timescale > 0 && duration > 0 ? TimeSpan.FromSeconds((double)duration / timescale) : null;

        return new MediaInfo(container, length, tracks);
    }


    private static MediaTrack? Track(byte[] d, Box trak) {
        if (Child(d, trak, "mdia") is not { } mdia
            || Child(d, mdia, "hdlr") is not { } hdlr || hdlr.Length < 12
            || Find(d, mdia, "minf", "stbl") is not { } stbl
            || Child(d, stbl, "stsd") is not { } stsd) {
            return null;
        }

        string handler = Bytes.Ascii(d, hdlr.Start + 8);
        MediaTrackKind? kind = handler switch {
            "vide" => MediaTrackKind.Video,
            "soun" => MediaTrackKind.Audio,
            "sbtl" or "subt" or "text" or "clcp" => MediaTrackKind.Subtitle,
            _ => null,
        };
        if (kind is null) {
            return null;
        }

        bool enabled = true;
        int width = 0, height = 0;
        bool turned = false;
        if (Child(d, trak, "tkhd") is { } tkhd) {
            enabled = (d[tkhd.Start + 3] & 1) != 0;
            int matrix = tkhd.Start + (d[tkhd.Start] == 1 ? 52 : 40);
            if (matrix + 44 <= tkhd.End) {
                int a = Bytes.S32(d, matrix), b = Bytes.S32(d, matrix + 4);
                turned = a == 0 && Math.Abs(b) == FixedOne;
                width = (int)(Bytes.U32(d, matrix + 36) >> 16);
                height = (int)(Bytes.U32(d, matrix + 40) >> 16);
            }
        }
        // A chapter list is a text track nobody turns on.
        if (handler == "text" && !enabled) {
            return null;
        }

        long timescale = 0, duration = 0;
        string? language = null;
        if (Child(d, mdia, "mdhd") is { } mdhd) {
            (timescale, duration) = MediaHeader(d, mdhd);
            int at = mdhd.Start + (d[mdhd.Start] == 1 ? 32 : 20);
            if (at + 2 <= mdhd.End) {
                language = Language(Bytes.U16(d, at));
            }
        }

        if (Sample(d, stsd, kind.Value) is not { } track) {
            return null;
        }
        if (kind == MediaTrackKind.Video) {
            if (width <= 0 || height <= 0) {
                (width, height) = (track.Width, track.Height);
            }
            if (turned) {
                (width, height) = (height, width);
            }
            track = track with { Width = width, Height = height, FrameRate = FrameRate(d, stbl, timescale) };
        }

        long? bitrate = timescale > 0 && duration > 0 && SampleBytes(d, stbl) is { } bytes
            ? (long)(bytes * 8 / ((double)duration / timescale))
            : null;

        return track with {
            Language = kind == MediaTrackKind.Video ? null : language,
            Title = TrackName(d, trak),
            Bitrate = bitrate is > 0 ? bitrate : track.Bitrate,
        };
    }


    /// <summary>The first sample description: what codec, and what its configuration boxes add.</summary>
    private static MediaTrack? Sample(byte[] d, Box stsd, MediaTrackKind kind) {
        int entry = stsd.Start + 8;
        if (entry + 16 > stsd.End) {
            return null;
        }

        int entryEnd = (int)Math.Min(entry + (long)Bytes.U32(d, entry), stsd.End);
        string format = Bytes.Ascii(d, entry + 4);

        return kind switch {
            MediaTrackKind.Video when entry + 86 <= entryEnd => VideoSample(d, entry, entryEnd, format),
            MediaTrackKind.Audio when entry + 36 <= entryEnd => AudioSample(d, entry, entryEnd, format),
            MediaTrackKind.Subtitle => new MediaTrack(kind, SubtitleCodec(format)),
            _ => null,
        };
    }


    private static MediaTrack VideoSample(byte[] d, int entry, int end, string format) {
        string codec = VideoCodec(format);
        int? depth = null;
        string? hdr = null;
        long? bitrate = null;
        foreach (var box in Boxes(d, entry + 86, end)) {
            switch (box.Type) {
                case "hvcC":
                    depth = MediaCodecs.HevcBitDepth(d, box.Start, box.End);
                    break;

                case "av1C":
                    depth = MediaCodecs.Av1BitDepth(d, box.Start, box.End);
                    break;

                case "vpcC" when box.Start + 9 <= box.End:
                    depth = d[box.Start + 6] >> 4;
                    hdr ??= MediaCodecs.Transfer(d[box.Start + 8]);
                    break;

                case "colr" when box.Start + 8 <= box.End && Bytes.Ascii(d, box.Start) is "nclx" or "nclc":
                    hdr ??= MediaCodecs.Transfer(Bytes.U16(d, box.Start + 6));
                    break;

                case "dvcC" or "dvvC" or "dvwC":
                    hdr = "Dolby Vision";
                    break;

                case "btrt" when box.Start + 12 <= box.End:
                    bitrate = Bytes.U32(d, box.Start + 8);
                    break;

                case "esds":
                    if (Decoder(d, box) is { } oti) {
                        codec = oti switch {
                            0x21 => "H.264",
                            >= 0x60 and <= 0x65 => "MPEG-2",
                            0x6A => "MPEG-1",
                            0x6C => "Motion JPEG",
                            _ => codec,
                        };
                    }
                    break;

                case "sinf":
                    if (Child(d, box, "frma") is { Length: >= 4 } frma) {
                        codec = VideoCodec(Bytes.Ascii(d, frma.Start));
                    }
                    break;
            }
        }
        if (format is "dvh1" or "dvhe" or "dvav" or "dva1" or "dav1") {
            hdr = "Dolby Vision";
        }

        return new MediaTrack(MediaTrackKind.Video, codec) {
            Width = Bytes.U16(d, entry + 32),
            Height = Bytes.U16(d, entry + 34),
            BitDepth = depth,
            Hdr = hdr,
            Bitrate = bitrate is > 0 ? bitrate : null,
        };
    }


    private static MediaTrack AudioSample(byte[] d, int entry, int end, string format) {
        // QuickTime's sound description has three versions; ISO's is the
        // first of them, its version field reserved and zero.
        int version = Bytes.U16(d, entry + 16);
        int channels = Bytes.U16(d, entry + 24);
        int rate = (int)(Bytes.U32(d, entry + 32) >> 16);
        int children = entry + 36;
        if (version == 1) {
            children = entry + 52;
        } else if (version == 2 && entry + 72 <= end) {
            rate = (int)BitConverter.Int64BitsToDouble((long)Bytes.U64(d, entry + 40));
            channels = (int)Bytes.U32(d, entry + 48);
            children = entry + 72;
        }

        var track = new MediaTrack(MediaTrackKind.Audio, AudioCodec(format)) { Channels = channels, SampleRate = rate };

        return AudioBoxes(d, children, end, track);
    }


    /// <summary>What an audio description's boxes refine; QuickTime nests some of them in <c>wave</c>.</summary>
    private static MediaTrack AudioBoxes(byte[] d, int start, int end, MediaTrack track) {
        foreach (var box in Boxes(d, start, end)) {
            switch (box.Type) {
                case "wave":
                    track = AudioBoxes(d, box.Start, box.End, track);
                    break;

                case "esds":
                    track = Esds(d, box, track);
                    break;

                case "dac3" when box.Start + 3 <= box.End: {
                        // fscod(2) bsid(5) bsmod(3) acmod(3) lfeon(1) bit_rate_code(5)
                        int bits = (d[box.Start] << 16) | (d[box.Start + 1] << 8) | d[box.Start + 2];
                        int acmod = (bits >> 11) & 7;
                        bool lfe = ((bits >> 10) & 1) != 0;
                        int rateCode = (bits >> 5) & 0x1F;
                        int[] kbps = { 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 448, 512, 576, 640 };
                        track = track with {
                            Channels = MediaCodecs.Ac3Channels(acmod, lfe),
                            Bitrate = rateCode < kbps.Length ? kbps[rateCode] * 1000L : track.Bitrate,
                        };
                        break;
                    }

                case "dec3" when box.Start + 5 <= box.End: {
                        // data_rate(13) num_ind_sub(3), then the first
                        // substream: fscod(2) bsid(5) reserved(1) asvc(1)
                        // bsmod(3) acmod(3) lfeon(1)
                        int acmod = (d[box.Start + 3] >> 1) & 7;
                        bool lfe = (d[box.Start + 3] & 1) != 0;
                        track = track with {
                            Channels = MediaCodecs.Ac3Channels(acmod, lfe),
                            Bitrate = (Bytes.U16(d, box.Start) >> 3) * 1000L,
                        };
                        break;
                    }

                case "dOps" when box.Start + 2 <= box.End:
                    track = track with { Channels = d[box.Start + 1] };
                    break;

                case "sinf":
                    if (Child(d, box, "frma") is { Length: >= 4 } frma) {
                        track = track with { Codec = AudioCodec(Bytes.Ascii(d, frma.Start)) };
                    }
                    break;
            }
        }

        return track;
    }


    /// <summary>
    /// An MPEG-4 elementary stream descriptor: the object type says what
    /// <c>mp4a</c> really carries (AAC, MP3, AC-3...), and for AAC the
    /// decoder-specific config says which flavour and how many channels.
    /// </summary>
    private static MediaTrack Esds(byte[] d, Box esds, MediaTrack track) {
        if (Decoder(d, esds) is not { } oti) {
            return track;
        }

        string codec = oti switch {
            0x40 or 0x66 or 0x67 or 0x68 => "AAC",
            0x69 or 0x6B => "MP3",
            0xA5 => "AC-3",
            0xA6 => "E-AC-3",
            0xA9 => "DTS",
            0xAD => "Opus",
            0xDD => "Vorbis",
            _ => track.Codec,
        };
        track = track with { Codec = codec };
        if (codec == "AAC" && Descriptor(d, esds.Start + 4, esds.End, 0x05) is { } config
            && MediaCodecs.Aac(d, config.Start, config.End) is { } aac) {
            track = track with { Codec = aac.Codec, Channels = aac.Channels > 0 ? aac.Channels : track.Channels };
        }

        return track;
    }


    /// <summary>The object type indication of an <c>esds</c> box's decoder config.</summary>
    private static int? Decoder(byte[] d, Box esds) {
        return Descriptor(d, esds.Start + 4, esds.End, 0x04) is { Length: >= 1 } config ? d[config.Start] : null;
    }


    /// <summary>
    /// The first descriptor with <paramref name="tag"/> in an MPEG-4
    /// descriptor list, looking inside the ES descriptor (tag 3) and the
    /// decoder config (tag 4) on the way.
    /// </summary>
    private static Box? Descriptor(byte[] d, int start, int end, int tag) {
        int p = start;
        while (p + 2 <= end) {
            int found = d[p++];
            int size = 0;
            for (int i = 0; i < 4 && p < end; i++) {
                byte b = d[p++];
                size = (size << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) {
                    break;
                }
            }
            int bodyEnd = Math.Min(p + size, end);
            if (found == tag) {
                return new Box("", p, bodyEnd);
            }

            if (found == 0x03 && p + 3 <= bodyEnd) {
                // ES_ID(16), then flags for three optional fields.
                int flags = d[p + 2];
                int q = p + 3;
                if ((flags & 0x80) != 0) {
                    q += 2;
                }
                if ((flags & 0x40) != 0 && q < bodyEnd) {
                    q += 1 + d[q];
                }
                if ((flags & 0x20) != 0) {
                    q += 2;
                }

                return Descriptor(d, q, bodyEnd, tag);
            }
            if (found == 0x04) {
                // objectType(8) streamType(8) bufferSize(24) maxBitrate(32) avgBitrate(32)
                return Descriptor(d, p + 13, bodyEnd, tag);
            }
            p = bodyEnd;
        }

        return null;
    }


    /// <summary>Frames per second: samples over their summed durations (<c>stts</c>).</summary>
    private static double? FrameRate(byte[] d, Box stbl, long timescale) {
        if (timescale <= 0 || Child(d, stbl, "stts") is not { } stts || stts.Length < 8) {
            return null;
        }

        long samples = 0, ticks = 0;
        long count = Bytes.U32(d, stts.Start + 4);
        int p = stts.Start + 8;
        for (long i = 0; i < count && p + 8 <= stts.End; i++, p += 8) {
            long n = Bytes.U32(d, p);
            samples += n;
            ticks += n * Bytes.U32(d, p + 4);
        }

        return samples > 1 && ticks > 0 ? samples * (double)timescale / ticks : null;
    }


    /// <summary>The bytes of all the track's samples (<c>stsz</c>), or null when it lists none.</summary>
    private static long? SampleBytes(byte[] d, Box stbl) {
        if (Child(d, stbl, "stsz") is not { } stsz || stsz.Length < 12) {
            return null;
        }

        long size = Bytes.U32(d, stsz.Start + 4);
        long count = Bytes.U32(d, stsz.Start + 8);
        if (size != 0) {
            return count > 0 ? size * count : null;
        }

        long total = 0;
        int p = stsz.Start + 12;
        for (long i = 0; i < count && p + 4 <= stsz.End; i++, p += 4) {
            total += Bytes.U32(d, p);
        }

        return total > 0 ? total : null;
    }


    /// <summary>Timescale and duration of a movie or media header (<c>mvhd</c>, <c>mdhd</c>), versions 0 and 1.</summary>
    private static (long Timescale, long Duration) MediaHeader(byte[] d, Box box) {
        return d[box.Start] == 1
            ? (Bytes.U32(d, box.Start + 20), (long)Bytes.U64(d, box.Start + 24))
            : (Bytes.U32(d, box.Start + 12), Bytes.U32(d, box.Start + 16));
    }


    /// <summary>
    /// A packed ISO 639-2 code: three letters of five bits each, offset
    /// from 0x60. Values below 0x400 are QuickTime's old Macintosh codes,
    /// which name no language this footer would say.
    /// </summary>
    private static string? Language(int packed) {
        if (packed < 0x400) {
            return null;
        }

        var letters = new char[3];
        for (int i = 0; i < 3; i++) {
            int c = ((packed >> (10 - i * 5)) & 0x1F) + 0x60;
            if (c is < 'a' or > 'z') {
                return null;
            }
            letters[i] = (char)c;
        }
        string code = new(letters);

        return code == Undetermined ? null : code;
    }


    /// <summary>The track's own name (<c>udta/name</c>), as ffmpeg writes a stream's title.</summary>
    private static string? TrackName(byte[] d, Box trak) {
        if (Find(d, trak, "udta", "name") is not { Length: > 0 } name) {
            return null;
        }

        string text = System.Text.Encoding.UTF8.GetString(d, name.Start, name.Length).TrimEnd('\0').Trim();

        return text.Length > 0 ? text : null;
    }


    private static string VideoCodec(string format) {
        return format switch {
            "avc1" or "avc2" or "avc3" or "avc4" or "dvav" or "dva1" => "H.264",
            "hvc1" or "hev1" or "dvh1" or "dvhe" => "H.265",
            "av01" or "dav1" => "AV1",
            "vp08" => "VP8",
            "vp09" => "VP9",
            "mp4v" => "MPEG-4",
            "apch" => "ProRes 422 HQ",
            "apcn" => "ProRes 422",
            "apcs" => "ProRes 422 LT",
            "apco" => "ProRes 422 Proxy",
            "ap4h" => "ProRes 4444",
            "ap4x" => "ProRes 4444 XQ",
            "jpeg" or "mjpa" or "mjpb" => "Motion JPEG",
            "s263" or "h263" => "H.263",
            "dvc " or "dvcp" or "dvpp" or "dv5n" or "dv5p" => "DV",
            "m2v1" or "mp2v" => "MPEG-2",
            "raw " or "2vuy" or "yuv2" or "v210" => "Uncompressed",
            "cvid" => "Cinepak",
            "png " => "PNG",
            _ => format.Trim(),
        };
    }


    private static string AudioCodec(string format) {
        return format switch {
            "mp4a" => "AAC",
            "ac-3" => "AC-3",
            "ec-3" => "E-AC-3",
            "ac-4" => "AC-4",
            "Opus" => "Opus",
            "fLaC" => "FLAC",
            "alac" => "ALAC",
            "sowt" or "twos" or "lpcm" or "in24" or "in32" or "fl32" or "fl64" or "raw " or "NONE" or "ipcm" or "fpcm" => "PCM",
            ".mp3" => "MP3",
            "ulaw" => "µ-law",
            "alaw" => "A-law",
            "samr" => "AMR",
            "sawb" => "AMR-WB",
            "dtsc" or "dtsh" or "dtsl" or "dtse" or "dtsx" => "DTS",
            "mlpa" => "TrueHD",
            "ima4" => "ADPCM",
            _ => format.Trim(),
        };
    }


    private static string SubtitleCodec(string format) {
        return format switch {
            "tx3g" => "Timed Text",
            "text" => "Text",
            "wvtt" => "WebVTT",
            "stpp" => "TTML",
            "c608" => "CEA-608",
            "c708" => "CEA-708",
            _ => format.Trim(),
        };
    }


    // --- Boxes ---


    private static IEnumerable<Box> Boxes(byte[] d, int start, int end) {
        int p = start;
        while (p + 8 <= end) {
            long size = Bytes.U32(d, p);
            string type = Bytes.Ascii(d, p + 4);
            int header = 8;
            if (size == 1) {
                if (p + 16 > end) {
                    yield break;
                }
                size = (long)Bytes.U64(d, p + 8);
                header = 16;
            } else if (size == 0) {
                size = end - p;
            }
            if (size < header || size > end - p) {
                yield break;
            }

            yield return new Box(type, p + header, (int)(p + size));
            p += (int)size;
        }
    }


    private static Box? Child(byte[] d, Box parent, string type) {
        foreach (var box in Boxes(d, parent.Start, parent.End)) {
            if (box.Type == type) {
                return box;
            }
        }

        return null;
    }


    private static Box? Find(byte[] d, Box parent, params string[] path) {
        Box? box = parent;
        foreach (string type in path) {
            box = box is { } b ? Child(d, b, type) : null;
        }

        return box;
    }


    /// <summary>A box's payload: <see cref="Start"/> is past its header.</summary>
    private readonly record struct Box(string Type, int Start, int End) {
        public int Length => End - Start;
    }
}
