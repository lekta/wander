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
    /// One file. Recycle-bin items (<c>OriginalLocation</c> set) get
    /// "Deleted" instead of "Modified" and a second line with the source
    /// folder, so the user can decide whether to restore them without
    /// context-switching.
    /// </summary>
    public static string ForFile(FileSystemEntry e, ImageMetadata? metadata) {
        string timeLabel = e.OriginalLocation is not null ? Strings.SummaryDeleted : Strings.SummaryModified;
        string summary = $"📄  {e.Name}\n{Strings.SummarySize}: {SizeFormatter.Format(e.Size)}   •   {timeLabel}: {TimeFormat.FromUtc(e.ModifiedUtc)}";
        if (e.OriginalLocation is not null) {
            summary += $"\n{Strings.SummaryDeletedFrom}: {e.OriginalLocation}";
        }
        // Which container this is in. The path in the address bar says it
        // too, but the footer is where the file is described, and "no
        // preview" reads very differently once you know why.
        if (Archives.Of(e.FullPath) is { IsRoot: false } archive) {
            summary += $"\n{Strings.SummaryInsideArchive}: {archive.Archive}";
        }
        if (metadata is { } m) {
            summary += "\n" + FormatExif(m);
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
    /// What the camera recorded, in the order a photographer reads it:
    /// body, then exposure, then pixels, then when. Anything the file does
    /// not carry is simply absent rather than blank.
    /// </summary>
    private static string FormatExif(ImageMetadata m) {
        var parts = new List<string>();
        string? camera = string.Join(" ", new[] { m.CameraMake, m.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(camera)) {
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
            parts.Add(string.Join(", ", shot));
        }
        if (m.PixelWidth is int w && m.PixelHeight is int h) {
            parts.Add($"{w} × {h}");
        }
        if (m.DateTaken is { } dt) {
            parts.Add(TimeFormat.Local(dt));
        }

        return string.Join("   •   ", parts);
    }
}
