using System.Globalization;
using System.IO;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Preview;
using Wander.Core.Shell;

namespace Wander.App.Preview;

/// <summary>
/// The caption under the preview: what is selected, said in words. Four
/// answers to one question — one file, one folder, many things, or nothing
/// but the folder we are standing in — and each is a string built from
/// facts the caller already has.
///
/// <para>
/// Separate from the controller because it is formatting and nothing else:
/// no state, no dispatcher, no cancellation except in the one place that
/// walks a tree (<see cref="CountAndSum"/>).
/// </para>
/// </summary>
internal static class SummaryText {
    /// <summary>
    /// What stands between two facts on a line: room, and nothing drawn in
    /// it. The lines used to be strung on centred dots and led by labels
    /// ("Size:", "Modified:"); a size and a date say what they are by
    /// themselves, and the dots were the most frequent glyph in the footer
    /// (2026-09-21, after the info line of FastStone).
    /// </summary>
    internal const string Gap = "   ";

    /// <summary>Between the parts of one fact - the four numbers of an exposure.</summary>
    private const string InnerGap = "  ";

    /// <summary>Audio tracks of a video given a line each; the rest are counted on one more.</summary>
    private const int MaxAudioLines = 3;

    /// <summary>Subtitle tracks named on their line; the rest are counted after them.</summary>
    private const int MaxSubtitles = 8;


    /// <summary>
    /// Debug menu: every camera is named by one stand-in word, so a
    /// screenshot does not advertise the body it was shot with. Session only.
    /// </summary>
    internal static bool MaskCamera { get; set; }


    /// <summary>
    /// One file: its name, then what it is - pixels when it is a picture or
    /// a video, the length of a video, size, when it was changed - then
    /// what the camera recorded, or a video's streams. The first
    /// line is the name alone: the footer draws the mention of the file's
    /// sidecars after it (<c>PreviewController.SummaryNote</c>).
    /// Recycle-bin items (<c>OriginalLocation</c> set) say "Deleted" before
    /// the date - the one date here that is not the obvious one - and get a
    /// line with the source folder, so the user can decide whether to
    /// restore them without context-switching.
    /// </summary>
    public static string ForFile(FileSystemEntry e, ImageMetadata? metadata, MediaInfo? media = null) {
        string when = TimeFormat.FromUtc(e.ModifiedUtc);
        var facts = new List<string>();
        if (metadata is { PixelWidth: int w, PixelHeight: int h }) {
            facts.Add($"{w} × {h}");
        } else if (media?.Video is { Width: > 0, Height: > 0 } video) {
            facts.Add($"{video.Width} × {video.Height}");
        }
        if (media?.Duration is { } length) {
            facts.Add(Timecode.Format(length, roundUp: true));
        }
        facts.Add(SizeFormatter.Format(e.Size));
        facts.Add(e.OriginalLocation is not null ? $"{Strings.SummaryDeleted}: {when}" : when);

        string summary = $"📄  {e.Name}\n{string.Join(Gap, facts)}";
        if (media is not null && ForMedia(media) is { Length: > 0 } streams) {
            summary += "\n" + streams;
        }
        if (e.OriginalLocation is not null) {
            summary += $"\n{Strings.SummaryDeletedFrom}: {e.OriginalLocation}";
        }
        // Which container this is in. The path in the address bar says it
        // too, but the footer is where the file is described, and "no
        // preview" reads very differently once you know why.
        if (Archives.Of(e.FullPath) is { IsRoot: false } archive) {
            summary += $"\n{Strings.SummaryInsideArchive}: {archive.Archive}";
        }
        if (metadata is { } m && FormatExif(m, when) is { Length: > 0 } exif) {
            summary += "\n" + exif;
        }

        return summary;
    }


    /// <summary>
    /// One folder. Counts and sizes are the census panel's job — it walks
    /// the tree once — and repeating them here meant walking it twice and
    /// printing the same numbers twice.
    /// </summary>
    public static string ForFolder(FileSystemEntry e) {
        return e.OriginalLocation is not null
            ? $"📁  {e.Name}\n{Strings.SummaryDeleted}: {TimeFormat.FromUtc(e.ModifiedUtc)}\n{Strings.SummaryDeletedFrom}: {e.OriginalLocation}"
            : $"📁  {e.Name}";
    }


    /// <summary>The folder we are standing in, when nothing is selected.</summary>
    public static string ForCurrentFolder(string path, string name) {
        return $"📁  {(string.IsNullOrEmpty(name) ? path : name)}";
    }


    /// <summary>
    /// Everything under a multi-item selection, counted and added up. No
    /// census panel appears for a mixed selection, so this is where the
    /// aggregate is said.
    /// </summary>
    public static (int Count, long Size) CountAndSum(string[] paths, CancellationToken ct) {
        int count = 0;
        long size = 0;
        foreach (var p in paths) {
            if (ct.IsCancellationRequested) {
                break;
            }
            try {
                if (Directory.Exists(p)) {
                    // Files only, and no descent into junctions or symlinks:
                    // a reparse loop (deep\l01\l02\loop -> l01) was an
                    // endless count until the selection changed, and a link
                    // into a sibling folder counted it twice. Files that are
                    // reparse points themselves (OneDrive placeholders) still
                    // count, which is why this is a recurse predicate and not
                    // AttributesToSkip; hidden and system files count as they
                    // always did. Length comes with the entry - no FileInfo
                    // per file.
                    // Fully qualified: System.IO.Enumeration has its own
                    // FileSystemEntry, and a using would make ours ambiguous.
                    var files = new System.IO.Enumeration.FileSystemEnumerable<long>(
                        p,
                        (ref System.IO.Enumeration.FileSystemEntry entry) => entry.Length,
                        new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }) {
                        ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry entry) => !entry.IsDirectory,
                        ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0,
                    };
                    foreach (long length in files) {
                        if (ct.IsCancellationRequested) {
                            break;
                        }
                        count++;
                        size += length;
                    }
                } else if (File.Exists(p)) {
                    count++;
                    try {
                        size += new FileInfo(p).Length;
                    } catch {
                        // ignore
                    }
                }
            } catch {
                // access denied on enumeration — skip this root
            }
        }

        return (count, size);
    }


    /// <summary>
    /// Several items, on one line: how many, their sidecars, the files
    /// inside when folders are among them, the pictures when not all of
    /// them are, the size. A number is said once - "23 selected, 23 files
    /// inside, 23 pictures" was the same 23 three times (2026-09-22) - and
    /// the EXIF the pictures share goes on a second line.
    /// </summary>
    /// <param name="selected">The rows selected.</param>
    /// <param name="companions">Sidecar paths folded into those rows; counted in the size.</param>
    /// <param name="filesInside">Files counted under the selection, sidecars included; said only when <paramref name="hasFolders"/>.</param>
    /// <param name="shots">What the pictures among the selection share, or null when there are none.</param>
    /// <param name="read">How many pictures were actually opened for it - fewer than <c>Shots</c> when the selection was capped.</param>
    public static string ForSelection(
        int selected, IReadOnlyList<string> companions, bool hasFolders, int filesInside, long size,
        ShotSummary? shots, int read) {
        string head = string.Format(Strings.SummarySelected, selected);
        if (companions.Count > 0) {
            // "(+23 .xmp)" - the single file's footer says "(+.xmp)", this
            // is the same mark with a count.
            var kinds = companions
                .Select(c => Path.GetExtension(c) is { Length: > 0 } ext ? ext : Path.GetFileName(c))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            head += $" (+{companions.Count} {string.Join(", ", kinds)})";
        }
        var facts = new List<string> { head };
        if (hasFolders) {
            facts.Add(string.Format(Strings.SummaryFilesInside, filesInside));
        }
        if (shots is not null && shots.Shots != selected) {
            facts.Add(string.Format(Strings.SummaryShots, shots.Shots));
        }
        facts.Add(SizeFormatter.Format(size));
        string text = string.Join(Gap, facts);
        if (shots is not null && ForShots(shots, read) is { Length: > 0 } exif) {
            text += "\n" + exif;
        }

        return text;
    }


    /// <summary>
    /// What several pictures have in common, under the count. The same
    /// order as one picture's line - body, exposure, pixels - with the two
    /// or three values a field is allowed to vary over listed after a
    /// comma, and a field that varies more than that left out
    /// (<see cref="ShotSummary"/>). Empty when nothing is shared and the
    /// EXIF was read in full.
    /// </summary>
    private static string ForShots(ShotSummary shots, int read) {
        var parts = new List<string>();
        if (shots.Cameras.Count > 0) {
            parts.Add(MaskCamera ? Strings.SummaryCameraMask : string.Join(", ", shots.Cameras));
        }
        if (shots.Iso.Count > 0) {
            parts.Add("ISO " + string.Join(", ", shots.Iso));
        }
        if (shots.Apertures.Count > 0) {
            parts.Add(string.Join(", ", shots.Apertures));
        }
        if (shots.Shutters.Count > 0) {
            parts.Add(string.Join(", ", shots.Shutters));
        }
        if (shots.FocalLengths.Count > 0) {
            parts.Add(string.Join(", ", shots.FocalLengths));
        }
        if (shots.PixelSizes.Count > 0) {
            parts.Add(string.Join(", ", shots.PixelSizes.Select(p => $"{p.Width} × {p.Height}")));
        }
        if (read < shots.Shots) {
            parts.Add(string.Format(Strings.SummaryShotsSample, read));
        }

        return string.Join(Gap, parts);
    }


    /// <summary>
    /// A video's streams, under its facts: the picture on one line - codec,
    /// depth past 8 bits, HDR, frame rate, bitrate (the stream's own, else
    /// the whole file's) - then a line per audio track and one for all the
    /// subtitles, each with its language as the file tags it.
    /// </summary>
    private static string ForMedia(MediaInfo media) {
        var lines = new List<string>();
        if (media.Video is { } video) {
            var codec = new List<string> { video.Codec };
            if (video.BitDepth is > 8 and int depth) {
                codec.Add(string.Format(Strings.MediaBitDepth, depth));
            }
            if (video.Hdr is { } hdr) {
                codec.Add(hdr);
            }
            var facts = new List<string> { string.Join(InnerGap, codec) };
            if (video.FrameRate is > 0 and double fps) {
                facts.Add(string.Format(Strings.MediaFps, Rate(fps)));
            }
            if ((video.Bitrate ?? media.Bitrate) is > 0 and long bps) {
                facts.Add(Bitrate(bps));
            }
            lines.Add(string.Join(Gap, facts));
        }

        var audio = media.Tracks.Where(t => t.Kind == MediaTrackKind.Audio).ToList();
        foreach (var track in audio.Take(MaxAudioLines)) {
            lines.Add($"{Strings.MediaAudio}: {AudioTrack(track)}");
        }
        if (audio.Count > MaxAudioLines) {
            lines.Add($"{Strings.MediaAudio}: {string.Format(Strings.AndMore, audio.Count - MaxAudioLines)}");
        }

        var subtitles = media.Tracks.Where(t => t.Kind == MediaTrackKind.Subtitle).ToList();
        if (subtitles.Count > 0) {
            var named = subtitles.Take(MaxSubtitles).Select(SubtitleTrack).ToList();
            if (subtitles.Count > MaxSubtitles) {
                named.Add(string.Format(Strings.AndMore, subtitles.Count - MaxSubtitles));
            }
            lines.Add($"{Strings.MediaSubtitles}: {string.Join(", ", named)}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>"AC-3 5.1   48 kHz   640 kbps   rus   "Dub"".</summary>
    private static string AudioTrack(MediaTrack track) {
        var facts = new List<string>();
        string? layout = track.Channels switch {
            1 => Strings.MediaMono,
            2 => Strings.MediaStereo,
            6 => "5.1",
            8 => "7.1",
            > 0 => string.Format(Strings.MediaChannels, track.Channels),
            _ => null,
        };
        facts.Add(layout is null ? track.Codec : $"{track.Codec} {layout}");
        if (track.SampleRate > 0) {
            facts.Add(string.Format(Strings.MediaKhz, (track.SampleRate / 1000.0).ToString("0.#", CultureInfo.CurrentCulture)));
        }
        if (track.Bitrate is > 0 and long bps) {
            facts.Add(Bitrate(bps));
        }
        if (track.Language is { } language) {
            facts.Add(language);
        }
        if (track.Title is { } title) {
            facts.Add(string.Format(Strings.MediaTitle, title));
        }

        return string.Join(Gap, facts);
    }

    /// <summary>"rus SRT "Signs" (forced)".</summary>
    private static string SubtitleTrack(MediaTrack track) {
        string text = track.Language is { } language ? $"{language} {track.Codec}" : track.Codec;
        if (track.Title is { } title) {
            text += " " + string.Format(Strings.MediaTitle, title);
        }
        if (track.IsForced) {
            text += " " + Strings.MediaForced;
        }

        return text;
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


    /// <summary>
    /// What the camera recorded, in the order a photographer reads it:
    /// body, then exposure, then when. Anything the file does not carry is
    /// simply absent rather than blank; the pixels are on the line above,
    /// and the moment it was taken is left out when it is the date already
    /// standing there (<paramref name="shownDate"/>) - a frame straight off
    /// the card, which is most of them.
    /// </summary>
    private static string FormatExif(ImageMetadata m, string shownDate) {
        var parts = new List<string>();
        if (ShotSummary.CameraName(m) is { } camera) {
            parts.Add(MaskCamera ? Strings.SummaryCameraMask : camera);
        }
        var shot = new List<string>();
        if (!string.IsNullOrEmpty(m.IsoSpeed)) {
            shot.Add($"ISO {m.IsoSpeed}");
        }
        if (!string.IsNullOrEmpty(m.Aperture)) {
            shot.Add(m.Aperture);
        }
        if (!string.IsNullOrEmpty(m.ShutterSpeed)) {
            shot.Add(m.ShutterSpeed);
        }
        if (!string.IsNullOrEmpty(m.FocalLength)) {
            shot.Add(m.FocalLength);
        }
        if (shot.Count > 0) {
            parts.Add(string.Join(InnerGap, shot));
        }
        if (m.DateTaken is { } dt && TimeFormat.Local(dt) != shownDate) {
            parts.Add(TimeFormat.Local(dt));
        }

        return string.Join(Gap, parts);
    }
}
