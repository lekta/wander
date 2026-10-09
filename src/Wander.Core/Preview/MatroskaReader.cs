namespace Wander.Core.Preview;

/// <summary>
/// Matroska and WebM: EBML, a tree of elements, each a variable-length ID
/// and a variable-length size. The segment's header elements - <c>Info</c>
/// (the length), <c>Tracks</c>, <c>Tags</c> (the per-track bitrates
/// mkvmerge writes) - are read whole; clusters of media are never entered.
///
/// <para>
/// The header elements usually stand before the first cluster. When one
/// does not - <c>Tags</c> is written at the end of the file - the
/// <c>SeekHead</c> says where it is, and that is the only jump made: the
/// clusters are not walked, a ten-gigabyte file is thousands of them.
/// </para>
/// </summary>
internal static class MatroskaReader {
    /// <summary>A header element past this is not one worth reading for a footer.</summary>
    private const long MaxElement = 16L * 1024 * 1024;

    private const uint EbmlHeader = 0x1A45DFA3;
    private const uint DocType = 0x4282;
    private const uint Segment = 0x18538067;
    private const uint SeekHead = 0x114D9B74;
    private const uint Seek = 0x4DBB;
    private const uint SeekId = 0x53AB;
    private const uint SeekPosition = 0x53AC;
    private const uint Info = 0x1549A966;
    private const uint TimestampScale = 0x2AD7B1;
    private const uint Duration = 0x4489;
    private const uint Tracks = 0x1654AE6B;
    private const uint TrackEntry = 0xAE;
    private const uint TrackType = 0x83;
    private const uint TrackUid = 0x73C5;
    private const uint FlagForced = 0x55AA;
    private const uint Name = 0x536E;
    private const uint Language = 0x22B59C;
    private const uint LanguageBcp47 = 0x22B59D;
    private const uint CodecId = 0x86;
    private const uint CodecPrivate = 0x63A2;
    private const uint DefaultDuration = 0x23E383;
    private const uint Video = 0xE0;
    private const uint PixelWidth = 0xB0;
    private const uint PixelHeight = 0xBA;
    private const uint Colour = 0x55B0;
    private const uint BitsPerChannel = 0x55B2;
    private const uint TransferCharacteristics = 0x55BA;
    private const uint BlockAdditionMapping = 0x41E4;
    private const uint BlockAddIdType = 0x41E7;
    private const uint Audio = 0xE1;
    private const uint SamplingFrequency = 0xB5;
    private const uint OutputSamplingFrequency = 0x78B5;
    private const uint Channels = 0x9F;
    private const uint Tags = 0x1254C367;
    private const uint Tag = 0x7373;
    private const uint Targets = 0x63C0;
    private const uint TagTrackUid = 0x63C5;
    private const uint SimpleTag = 0x67C8;
    private const uint TagName = 0x45A3;
    private const uint TagString = 0x4487;
    private const uint Cluster = 0x1F43B675;

    /// <summary>Dolby Vision configuration in a block addition mapping: <c>dvcC</c>, <c>dvvC</c>.</summary>
    private const ulong DolbyVisionConfig = 0x64766343;
    private const ulong DolbyVisionConfigV2 = 0x64767643;


    public static MediaInfo? Read(Stream stream) {
        if (Header(stream, 0) is not { Id: EbmlHeader, Size: >= 0 } ebml) {
            return null;
        }

        byte[] head = Payload(stream, ebml);
        string docType = Children(head, 0, head.Length).Where(e => e.Id == DocType)
            .Select(e => Text(head, e)).FirstOrDefault() ?? "matroska";

        long position = ebml.DataStart + ebml.Size;
        if (Header(stream, position) is not { Id: Segment } segment) {
            return null;
        }

        long segmentEnd = segment.Size < 0 ? stream.Length : Math.Min(segment.DataStart + segment.Size, stream.Length);
        byte[]? info = null, tracks = null, tags = null;
        var seeks = new Dictionary<uint, long>();
        position = segment.DataStart;
        while (position < segmentEnd && Header(stream, position) is { } element) {
            if (element.Id == Cluster || element.Size < 0) {
                break;
            }

            switch (element.Id) {
                case SeekHead:
                    ReadSeeks(Payload(stream, element), seeks);
                    break;

                case Info:
                    info = Payload(stream, element);
                    break;

                case Tracks:
                    tracks = Payload(stream, element);
                    break;

                case Tags:
                    tags = Payload(stream, element);
                    break;
            }
            position = element.DataStart + element.Size;
        }

        info ??= Sought(stream, segment, seeks, Info);
        tracks ??= Sought(stream, segment, seeks, Tracks);
        tags ??= Sought(stream, segment, seeks, Tags);
        if (tracks is null) {
            return null;
        }

        TimeSpan? duration = null;
        if (info is not null) {
            ulong scale = 1_000_000;
            double? units = null;
            foreach (var e in Children(info, 0, info.Length)) {
                switch (e.Id) {
                    case TimestampScale:
                        scale = UInt(info, e);
                        break;

                    case Duration:
                        units = Float(info, e);
                        break;
                }
            }
            if (units is > 0) {
                duration = TimeSpan.FromSeconds(units.Value * scale / 1e9);
            }
        }

        var bitrates = tags is null ? new Dictionary<ulong, long>() : TrackBitrates(tags);
        var list = new List<MediaTrack>();
        foreach (var e in Children(tracks, 0, tracks.Length)) {
            if (e.Id == TrackEntry && Track(tracks, e, bitrates) is { } track) {
                list.Add(track);
            }
        }

        return new MediaInfo(docType == "webm" ? "WebM" : "Matroska", duration, list);
    }


    private static MediaTrack? Track(byte[] d, Element entry, Dictionary<ulong, long> bitrates) {
        ulong type = 0, uid = 0, frameNs = 0;
        string codecId = "";
        string language = "eng";
        string? bcp47 = null, name = null;
        bool forced = false, dolby = false;
        Element? codecPrivate = null, video = null, audio = null;
        foreach (var e in Children(d, entry.DataStart, entry.End)) {
            switch (e.Id) {
                case TrackType:
                    type = UInt(d, e);
                    break;

                case TrackUid:
                    uid = UInt(d, e);
                    break;

                case CodecId:
                    codecId = Text(d, e);
                    break;

                case CodecPrivate:
                    codecPrivate = e;
                    break;

                case Language:
                    language = Text(d, e);
                    break;

                case LanguageBcp47:
                    bcp47 = Text(d, e);
                    break;

                case Name:
                    name = Text(d, e);
                    break;

                case FlagForced:
                    forced = UInt(d, e) != 0;
                    break;

                case DefaultDuration:
                    frameNs = UInt(d, e);
                    break;

                case Video:
                    video = e;
                    break;

                case Audio:
                    audio = e;
                    break;

                case BlockAdditionMapping:
                    dolby |= Children(d, e.DataStart, e.End)
                        .Any(m => m.Id == BlockAddIdType && UInt(d, m) is DolbyVisionConfig or DolbyVisionConfigV2);
                    break;
            }
        }

        MediaTrackKind? kind = type switch {
            1 => MediaTrackKind.Video,
            2 => MediaTrackKind.Audio,
            0x11 => MediaTrackKind.Subtitle,
            _ => null,
        };
        if (kind is null) {
            return null;
        }

        string lang = bcp47 ?? language;
        var track = new MediaTrack(kind.Value, Codec(codecId, d, codecPrivate)) {
            Language = kind == MediaTrackKind.Video || lang is "" or "und" ? null : lang,
            Title = string.IsNullOrWhiteSpace(name) ? null : name,
            IsForced = forced,
            Bitrate = bitrates.TryGetValue(uid, out long bps) ? bps : null,
        };

        if (kind == MediaTrackKind.Video) {
            track = VideoFacts(d, video, codecPrivate, track) with {
                FrameRate = frameNs > 0 ? 1e9 / frameNs : null,
            };
            if (dolby) {
                track = track with { Hdr = "Dolby Vision" };
            }
        } else if (kind == MediaTrackKind.Audio) {
            track = AudioFacts(d, audio, codecPrivate, track);
        }

        return track;
    }


    private static MediaTrack VideoFacts(byte[] d, Element? video, Element? codecPrivate, MediaTrack track) {
        int? depth = null;
        string? hdr = null;
        if (video is { } v) {
            foreach (var e in Children(d, v.DataStart, v.End)) {
                switch (e.Id) {
                    case PixelWidth:
                        track = track with { Width = (int)UInt(d, e) };
                        break;

                    case PixelHeight:
                        track = track with { Height = (int)UInt(d, e) };
                        break;

                    case Colour:
                        foreach (var c in Children(d, e.DataStart, e.End)) {
                            if (c.Id == BitsPerChannel && UInt(d, c) is > 0UL and var bits) {
                                depth = (int)bits;
                            } else if (c.Id == TransferCharacteristics) {
                                hdr = MediaCodecs.Transfer((int)UInt(d, c));
                            }
                        }
                        break;
                }
            }
        }
        if (depth is null && codecPrivate is { } p) {
            depth = track.Codec switch {
                "H.265" => MediaCodecs.HevcBitDepth(d, (int)p.DataStart, p.End),
                "AV1" => MediaCodecs.Av1BitDepth(d, (int)p.DataStart, p.End),
                _ => null,
            };
        }

        return track with { BitDepth = depth, Hdr = hdr };
    }


    private static MediaTrack AudioFacts(byte[] d, Element? audio, Element? codecPrivate, MediaTrack track) {
        double rate = 8000;
        double? output = null;
        int channels = 1;
        if (audio is { } a) {
            foreach (var e in Children(d, a.DataStart, a.End)) {
                switch (e.Id) {
                    case SamplingFrequency:
                        rate = Float(d, e);
                        break;

                    case OutputSamplingFrequency:
                        output = Float(d, e);
                        break;

                    case Channels:
                        channels = (int)UInt(d, e);
                        break;
                }
            }
        }
        track = track with { SampleRate = (int)(output ?? rate), Channels = channels };
        if (track.Codec == "AAC" && codecPrivate is { } p && MediaCodecs.Aac(d, (int)p.DataStart, p.End) is { } aac) {
            track = track with { Codec = aac.Codec };
        }

        return track;
    }


    private static string Codec(string id, byte[] d, Element? codecPrivate) {
        int start = codecPrivate is { } p ? (int)p.DataStart : 0;
        int end = codecPrivate?.End ?? 0;
        if (id == "V_MS/VFW/FOURCC") {
            // A BITMAPINFOHEADER: the compression code at 16.
            return end - start >= 20 ? MediaCodecs.VideoFourcc(Bytes.Ascii(d, start + 16)) : id;
        }
        if (id == "A_MS/ACM") {
            return end - start >= 2 ? MediaCodecs.WaveFormat(d, start, end) : id;
        }
        if (id.StartsWith("A_AAC", StringComparison.Ordinal)) {
            return "AAC";
        }
        if (id.StartsWith("A_PCM", StringComparison.Ordinal)) {
            return "PCM";
        }
        if (id.StartsWith("V_MPEG4/ISO/", StringComparison.Ordinal) && id != "V_MPEG4/ISO/AVC") {
            return "MPEG-4";
        }
        if (id.StartsWith("A_DTS/", StringComparison.Ordinal)) {
            return "DTS-HD";
        }

        return id switch {
            "V_MPEG4/ISO/AVC" => "H.264",
            "V_MPEGH/ISO/HEVC" => "H.265",
            "V_AV1" => "AV1",
            "V_VP8" => "VP8",
            "V_VP9" => "VP9",
            "V_MPEG4/MS/V3" => "MS MPEG-4",
            "V_MPEG1" => "MPEG-1",
            "V_MPEG2" => "MPEG-2",
            "V_THEORA" => "Theora",
            "V_PRORES" => "ProRes",
            "V_MJPEG" => "Motion JPEG",
            "V_FFV1" => "FFV1",
            "V_UNCOMPRESSED" => "Uncompressed",
            "A_AC3" => "AC-3",
            "A_EAC3" => "E-AC-3",
            "A_DTS" => "DTS",
            "A_TRUEHD" => "TrueHD",
            "A_MLP" => "MLP",
            "A_OPUS" => "Opus",
            "A_VORBIS" => "Vorbis",
            "A_FLAC" => "FLAC",
            "A_ALAC" => "ALAC",
            "A_MPEG/L3" => "MP3",
            "A_MPEG/L2" => "MP2",
            "A_MPEG/L1" => "MP1",
            "A_TTA1" => "TTA",
            "A_WAVPACK4" => "WavPack",
            "S_TEXT/UTF8" or "S_TEXT/ASCII" => "SRT",
            "S_TEXT/SSA" or "S_SSA" => "SSA",
            "S_TEXT/ASS" or "S_ASS" => "ASS",
            "S_TEXT/WEBVTT" => "WebVTT",
            "S_TEXT/USF" => "USF",
            "S_HDMV/PGS" => "PGS",
            "S_HDMV/TEXTST" => "TextST",
            "S_VOBSUB" => "VobSub",
            "S_DVBSUB" => "DVB",
            "S_KATE" => "Kate",
            "S_ARIBSUB" => "ARIB",
            _ => id,
        };
    }


    /// <summary>
    /// The <c>BPS</c> statistics tag of each track, by its UID - what
    /// mkvmerge measured while writing the file.
    /// </summary>
    private static Dictionary<ulong, long> TrackBitrates(byte[] d) {
        var result = new Dictionary<ulong, long>();
        foreach (var tag in Children(d, 0, d.Length).Where(e => e.Id == Tag)) {
            var uids = new List<ulong>();
            long? bps = null;
            foreach (var e in Children(d, tag.DataStart, tag.End)) {
                if (e.Id == Targets) {
                    uids.AddRange(Children(d, e.DataStart, e.End).Where(t => t.Id == TagTrackUid).Select(t => UInt(d, t)));
                } else if (e.Id == SimpleTag) {
                    string? name = null, value = null;
                    foreach (var s in Children(d, e.DataStart, e.End)) {
                        if (s.Id == TagName) {
                            name = Text(d, s);
                        } else if (s.Id == TagString) {
                            value = Text(d, s);
                        }
                    }
                    if (name == "BPS" && long.TryParse(value, out long parsed) && parsed > 0) {
                        bps = parsed;
                    }
                }
            }
            if (bps is { } b) {
                foreach (ulong uid in uids) {
                    result[uid] = b;
                }
            }
        }

        return result;
    }


    private static void ReadSeeks(byte[] d, Dictionary<uint, long> seeks) {
        foreach (var seek in Children(d, 0, d.Length).Where(e => e.Id == Seek)) {
            uint id = 0;
            long? at = null;
            foreach (var e in Children(d, seek.DataStart, seek.End)) {
                if (e.Id == SeekId) {
                    id = (uint)UInt(d, e);
                } else if (e.Id == SeekPosition) {
                    at = (long)UInt(d, e);
                }
            }
            if (at is { } position) {
                seeks.TryAdd(id, position);
            }
        }
    }


    /// <summary>The element the seek head points at, read, when it is the one asked for.</summary>
    private static byte[]? Sought(Stream stream, Element segment, Dictionary<uint, long> seeks, uint id) {
        if (!seeks.TryGetValue(id, out long offset)) {
            return null;
        }

        return Header(stream, segment.DataStart + offset) is { Size: >= 0 } element && element.Id == id
            ? Payload(stream, element)
            : null;
    }


    // --- EBML ---


    /// <summary>The element header at <paramref name="position"/>; size -1 when it is unknown (a live stream).</summary>
    private static Element? Header(Stream stream, long position) {
        if (position < 0 || position >= stream.Length) {
            return null;
        }

        stream.Position = position;
        int first = stream.ReadByte();
        int idLength = first switch {
            >= 0x80 => 1,
            >= 0x40 => 2,
            >= 0x20 => 3,
            >= 0x10 => 4,
            _ => 0,
        };
        if (idLength == 0) {
            return null;
        }

        uint id = (uint)first;
        for (int i = 1; i < idLength; i++) {
            id = (id << 8) | (uint)ReadByte(stream);
        }

        int sizeFirst = ReadByte(stream);
        int sizeLength = 1;
        while (sizeLength <= 8 && (sizeFirst & (0x80 >> (sizeLength - 1))) == 0) {
            sizeLength++;
        }
        if (sizeLength > 8) {
            return null;
        }

        ulong size = (ulong)(sizeFirst & (0xFF >> sizeLength));
        bool allOnes = size == (ulong)(0xFF >> sizeLength);
        for (int i = 1; i < sizeLength; i++) {
            int b = ReadByte(stream);
            size = (size << 8) | (uint)b;
            allOnes &= b == 0xFF;
        }

        long dataStart = position + idLength + sizeLength;

        return new Element(id, dataStart, allOnes ? -1 : (long)size, 0);
    }


    private static int ReadByte(Stream stream) {
        int b = stream.ReadByte();

        return b >= 0 ? b : throw new EndOfStreamException();
    }


    private static byte[] Payload(Stream stream, Element element) {
        if (element.Size > MaxElement || element.DataStart + element.Size > stream.Length) {
            throw new InvalidOperationException("Matroska header element out of bounds");
        }

        var bytes = new byte[element.Size];
        stream.Position = element.DataStart;
        stream.ReadExactly(bytes);

        return bytes;
    }


    /// <summary>The elements between <paramref name="start"/> and <paramref name="end"/> of a payload in memory.</summary>
    private static IEnumerable<Element> Children(byte[] d, long start, long end) {
        int p = (int)start;
        while (p < end) {
            int first = d[p];
            int idLength = first >= 0x80 ? 1 : first >= 0x40 ? 2 : first >= 0x20 ? 3 : first >= 0x10 ? 4 : 0;
            if (idLength == 0 || p + idLength >= end) {
                yield break;
            }

            uint id = 0;
            for (int i = 0; i < idLength; i++) {
                id = (id << 8) | d[p + i];
            }
            p += idLength;

            int sizeFirst = d[p];
            int sizeLength = 1;
            while (sizeLength <= 8 && (sizeFirst & (0x80 >> (sizeLength - 1))) == 0) {
                sizeLength++;
            }
            if (sizeLength > 8 || p + sizeLength > end) {
                yield break;
            }

            long size = sizeFirst & (0xFF >> sizeLength);
            for (int i = 1; i < sizeLength; i++) {
                size = (size << 8) | d[p + i];
            }
            p += sizeLength;
            if (size > end - p) {
                yield break;
            }

            yield return new Element(id, p, size, (int)(p + size));
            p += (int)size;
        }
    }


    private static ulong UInt(byte[] d, Element e) {
        ulong value = 0;
        for (int i = 0; i < e.Size && i < 8; i++) {
            value = (value << 8) | d[e.DataStart + i];
        }

        return value;
    }


    private static double Float(byte[] d, Element e) {
        return e.Size switch {
            4 => BitConverter.Int32BitsToSingle((int)Bytes.U32(d, (int)e.DataStart)),
            8 => BitConverter.Int64BitsToDouble((long)Bytes.U64(d, (int)e.DataStart)),
            _ => 0,
        };
    }


    private static string Text(byte[] d, Element e) {
        return System.Text.Encoding.UTF8.GetString(d, (int)e.DataStart, (int)e.Size).TrimEnd('\0');
    }


    /// <param name="End">Where the payload ends in a buffer; zero for a header read from the stream.</param>
    private readonly record struct Element(uint Id, long DataStart, long Size, int End);
}
