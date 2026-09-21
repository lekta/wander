using System.IO;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
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


    /// <summary>
    /// One file: its name, then what it is - pixels when it is a picture,
    /// size, when it was changed - then what the camera recorded. The first
    /// line is the name alone: the footer draws the mention of the file's
    /// sidecars after it (<c>PreviewController.SummaryNote</c>).
    /// Recycle-bin items (<c>OriginalLocation</c> set) say "Deleted" before
    /// the date - the one date here that is not the obvious one - and get a
    /// line with the source folder, so the user can decide whether to
    /// restore them without context-switching.
    /// </summary>
    public static string ForFile(FileSystemEntry e, ImageMetadata? metadata) {
        string when = TimeFormat.FromUtc(e.ModifiedUtc);
        var facts = new List<string>();
        if (metadata is { PixelWidth: int w, PixelHeight: int h }) {
            facts.Add($"{w} × {h}");
        }
        facts.Add(SizeFormatter.Format(e.Size));
        facts.Add(e.OriginalLocation is not null ? $"{Strings.SummaryDeleted}: {when}" : when);

        string summary = $"📄  {e.Name}\n{string.Join(Gap, facts)}";
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
    /// What several pictures have in common, under the count. The same
    /// order as one picture's line - body, exposure, pixels - with the two
    /// or three values a field is allowed to vary over listed after a
    /// comma, and a field that varies more than that left out
    /// (<see cref="ShotSummary"/>). Nothing is said when nothing is shared.
    /// </summary>
    /// <param name="read">How many pictures were actually opened for it - fewer than <c>Shots</c> when the selection was capped.</param>
    public static string ForShots(ShotSummary shots, int read) {
        string headline = read < shots.Shots
            ? string.Format(Strings.SummaryShotsSample, shots.Shots, read)
            : string.Format(Strings.SummaryShots, shots.Shots);
        if (shots.IsEmpty) {
            return headline;
        }

        var parts = new List<string>();
        if (shots.Cameras.Count > 0) {
            parts.Add(string.Join(", ", shots.Cameras));
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

        return headline + "\n" + string.Join(Gap, parts);
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
            parts.Add(camera);
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
