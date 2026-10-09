using System.Globalization;
using Wander.App.Resources;
using Wander.Core.Preview;

namespace Wander.App.Preview;

/// <summary>
/// One stream of a video in the footer's table: a glyph for its kind
/// (the word in its tooltip), its language, then codec, rate and bitrate
/// in columns lined up with the other rows, its name last and dim.
/// </summary>
public sealed record MediaRow(string Glyph, string Tip, string Language, string Main, string Rate, string Bitrate, string Title);


/// <summary>
/// A video's streams for the footer (<see cref="MediaProbe"/>): the
/// picture, a row per sound track, the subtitles on one line under them.
/// Rows rather than lines of text (2026-10-09): a film with three dubs
/// read as a paragraph, the same facts in different places on each line.
/// </summary>
internal static class MediaRows {
    /// <summary>Sound tracks given a row each; the rest are counted on one more.</summary>
    private const int MaxAudioRows = 4;

    /// <summary>Subtitle tracks named on their line; the rest are counted after them.</summary>
    private const int MaxSubtitles = 10;

    // Segoe MDL2 Assets: Video, Volume, ClosedCaption.
    private const string VideoGlyph = "";
    private const string AudioGlyph = "";

    /// <summary>Between the parts of one value - a codec and its depth.</summary>
    private const string InnerGap = "  ";


    public static IReadOnlyList<MediaRow> For(MediaInfo media) {
        var rows = new List<MediaRow>();
        if (media.Video is { } video) {
            var codec = new List<string> { video.Codec };
            if (video.BitDepth is > 8 and int depth) {
                codec.Add(string.Format(Strings.MediaBitDepth, depth));
            }
            if (video.Hdr is { } hdr) {
                codec.Add(hdr);
            }
            string rate = video.FrameRate is > 0 and double fps ? string.Format(Strings.MediaFps, Rate(fps)) : "";
            string bitrate = (video.Bitrate ?? media.Bitrate) is > 0 and long bps ? Bitrate(bps) : "";
            rows.Add(new MediaRow(VideoGlyph, Strings.MediaVideo, "", string.Join(InnerGap, codec), rate, bitrate, video.Title ?? ""));
        }

        var audio = media.Tracks.Where(t => t.Kind == MediaTrackKind.Audio).ToList();
        foreach (var track in audio.Take(MaxAudioRows)) {
            string? layout = track.Channels switch {
                1 => Strings.MediaMono,
                2 => Strings.MediaStereo,
                6 => "5.1",
                8 => "7.1",
                > 0 => string.Format(Strings.MediaChannels, track.Channels),
                _ => null,
            };
            rows.Add(new MediaRow(
                AudioGlyph,
                Strings.MediaAudio,
                track.Language ?? "",
                layout is null ? track.Codec : $"{track.Codec} {layout}",
                track.SampleRate > 0 ? string.Format(Strings.MediaKhz, (track.SampleRate / 1000.0).ToString("0.#", CultureInfo.CurrentCulture)) : "",
                track.Bitrate is > 0 and long bps ? Bitrate(bps) : "",
                track.Title ?? ""));
        }
        if (audio.Count > MaxAudioRows) {
            rows.Add(new MediaRow(AudioGlyph, Strings.MediaAudio, "", string.Format(Strings.AndMore, audio.Count - MaxAudioRows), "", "", ""));
        }

        return rows;
    }


    /// <summary>
    /// "ru Forced, ru Full, en SDH   SRT": each track by its language and
    /// name, the codec once when they share it, after each when they do not.
    /// Empty when there are none.
    /// </summary>
    public static string Subtitles(MediaInfo media) {
        var tracks = media.Tracks.Where(t => t.Kind == MediaTrackKind.Subtitle).ToList();
        if (tracks.Count == 0) {
            return "";
        }

        bool oneCodec = tracks.Select(t => t.Codec).Distinct().Count() == 1;
        var named = tracks.Take(MaxSubtitles).Select(t => {
            var parts = new List<string>();
            if (t.Language is { } language) {
                parts.Add(language);
            }
            if (!oneCodec) {
                parts.Add(t.Codec);
            }
            if (t.Title is { } title) {
                parts.Add(title);
            } else if (t.IsForced) {
                parts.Add(Strings.MediaForced);
            }

            return parts.Count > 0 ? string.Join(" ", parts) : t.Codec;
        }).ToList();
        if (tracks.Count > MaxSubtitles) {
            named.Add(string.Format(Strings.AndMore, tracks.Count - MaxSubtitles));
        }

        string list = string.Join(", ", named);

        return oneCodec ? $"{list}{SummaryText.Gap}{tracks[0].Codec}" : list;
    }


    /// <summary>"25", "23,976", "29,97": whole rates without a fraction, the NTSC ones with theirs.</summary>
    private static string Rate(double fps) {
        return Math.Abs(fps - Math.Round(fps)) < 0.005
            ? Math.Round(fps).ToString(CultureInfo.CurrentCulture)
            : fps.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static string Bitrate(long bitsPerSecond) {
        return bitsPerSecond < 1_000_000
            ? string.Format(Strings.MediaKbps, Math.Round(bitsPerSecond / 1000.0))
            : string.Format(Strings.MediaMbps, (bitsPerSecond / 1_000_000.0).ToString("0.#", CultureInfo.CurrentCulture));
    }
}
