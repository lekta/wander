using System.Buffers.Binary;

namespace Wander.Core.Preview;

/// <summary>
/// Ogg Vorbis and Opus: the first two packets of the first stream - the
/// codec's identification header (channels, rate) and its comment header
/// (Vorbis comments, as in FLAC, <see cref="FlacTags.ReadComments"/>) - and
/// the length from the granule position of the last page.
///
/// <para>
/// A page is "OggS", version, flags, a 64-bit granule position, the stream
/// serial, a sequence number, a CRC, then a table of segment lengths and the
/// segments. A packet is segments up to the first one shorter than 255
/// bytes, and runs on over pages - the comment packet with a cover in it
/// does. The CRC is not checked: a damaged page reads as nonsense and is
/// dropped as such.
/// </para>
/// </summary>
internal static class OggTags {
    private const int PageHeaderBytes = 27;

    /// <summary>Opus always decodes at 48 kHz, and its granule counts at that rate.</summary>
    private const int OpusRate = 48000;

    /// <summary>How far from the end the last page is looked for: a page is under 64 KB.</summary>
    private const int TailBytes = 64 * 1024;

    /// <summary>A comment packet past this is not a tag block (the cover cap and change).</summary>
    private const int MaxPacketBytes = 24 * 1024 * 1024;


    public static AudioTrackInfo? Read(Stream stream) {
        var packets = FirstPackets(stream, 2, out uint serial);
        if (packets.Count == 0) {
            return null;
        }

        byte[] head = packets[0];
        string codec;
        int channels, rate, preSkip = 0, commentStart;
        int? nominalKbps = null;
        if (head.Length >= 30 && head[0] == 1 && Bytes.Ascii(head, 1, 6) == "vorbis") {
            codec = "Vorbis";
            channels = head[11];
            rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(12));
            int nominal = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(20));
            nominalKbps = nominal > 0 ? (int)Math.Round(nominal / 1000.0) : null;
            commentStart = 7;
        } else if (head.Length >= 19 && Bytes.Ascii(head, 0, 8) == "OpusHead") {
            codec = "Opus";
            channels = head[9];
            preSkip = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(10));
            rate = OpusRate;
            commentStart = 8;
        } else {
            return null;
        }

        string? title = null, artist = null, album = null, year = null, track = null;
        byte[]? cover = null;
        if (packets.Count > 1 && IsCommentHeader(packets[1], codec)) {
            FlacTags.ReadComments(packets[1], commentStart, ref title, ref artist, ref album, ref year, ref track, ref cover);
        }

        long samples = (LastGranule(stream, serial) ?? 0) - preSkip;
        TimeSpan? duration = samples > 0 && rate > 0 ? TimeSpan.FromSeconds(samples / (double)rate) : null;
        int? bitrate = duration is { TotalSeconds: > 0 } d
            ? (int)Math.Round(stream.Length * 8 / d.TotalSeconds / 1000)
            : nominalKbps;

        return new AudioTrackInfo(
            title, artist, album, year, track, duration,
            rate > 0 ? rate : null, channels > 0 ? channels : null, bitrate, cover, codec);
    }


    private static bool IsCommentHeader(byte[] packet, string codec) {
        return codec == "Opus"
            ? packet.Length >= 8 && Bytes.Ascii(packet, 0, 8) == "OpusTags"
            : packet.Length >= 7 && packet[0] == 3 && Bytes.Ascii(packet, 1, 6) == "vorbis";
    }


    /// <summary>
    /// The first <paramref name="count"/> packets of the first stream, put
    /// together over pages; pages of other streams (a video beside the sound)
    /// are stepped over.
    /// </summary>
    private static List<byte[]> FirstPackets(Stream stream, int count, out uint serial) {
        var packets = new List<byte[]>();
        using var packet = new MemoryStream();
        uint? first = null;
        stream.Position = 0;
        while (packets.Count < count && packet.Length <= MaxPacketBytes) {
            byte[]? page = AudioTags.ReadExact(stream, PageHeaderBytes);
            if (page is null || Bytes.Ascii(page, 0) != "OggS") {
                break;
            }
            byte[]? lacing = AudioTags.ReadExact(stream, page[26]);
            if (lacing is null) {
                break;
            }

            uint pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(14));
            first ??= pageSerial;
            int body = lacing.Sum(b => b);
            if (pageSerial != first) {
                stream.Position += body;
                continue;
            }
            byte[]? data = AudioTags.ReadExact(stream, body);
            if (data is null) {
                break;
            }

            int at = 0;
            foreach (byte length in lacing) {
                packet.Write(data, at, length);
                at += length;
                if (length < 255) {
                    packets.Add(packet.ToArray());
                    packet.SetLength(0);
                    if (packets.Count == count) {
                        break;
                    }
                }
            }
        }
        serial = first ?? 0;

        return packets;
    }


    /// <summary>The granule position of the stream's last page that ends a packet; null when none is found.</summary>
    private static long? LastGranule(Stream stream, uint serial) {
        long start = Math.Max(0, stream.Length - TailBytes);
        stream.Position = start;
        if (AudioTags.ReadExact(stream, stream.Length - start) is not { } tail) {
            return null;
        }

        for (int i = tail.Length - PageHeaderBytes; i >= 0; i--) {
            if (tail[i] == 'O' && Bytes.Ascii(tail, i) == "OggS"
                && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 14)) == serial) {
                // -1: no packet ends on this page.
                long granule = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(i + 6));
                if (granule > 0) {
                    return granule;
                }
            }
        }

        return null;
    }
}
