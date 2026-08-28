using Wander.Core.Companions;
using Wander.Core.Icons;

namespace Wander.Core.FileSystem;

/// <summary>
/// Answers one question about a folder listing: is this a folder of
/// pictures? The gallery view switches itself on when it is (see
/// <c>MainViewModel.AutoSelectViewMode</c>).
///
/// <para>
/// The whole difficulty is the denominator. A folder of a hundred RAW files
/// also holds a hundred <c>.pp3</c> sidecars, a couple of
/// <c>.xmp</c>-and-<c>.bak</c> leftovers and whatever the camera's software
/// dropped there — counted naively, a pure photo folder scores 50% and the
/// gallery never appears. So only <em>content</em> files are counted:
/// sidecars belong to a picture rather than being one, and a backup is a
/// copy of something that is already in the count.
/// </para>
///
/// <para>
/// Folders are not counted at all, in either half. A shoot organised into
/// subfolders is still a shoot, and a folder that holds four photographs
/// and nothing else is a folder of photographs no matter how few they are —
/// hence a share test with no minimum count.
/// </para>
/// </summary>
public static class ImageFolderProbe {
    /// <summary>Share of content files that must be pictures when the caller doesn't say.</summary>
    public const int DefaultPercent = 50;

    /// <summary>
    /// Extensions that mean "a copy of, or a fragment of, another file
    /// here". None of them is content, whatever it is a copy of.
    /// </summary>
    private static readonly HashSet<string> _derived = new(StringComparer.OrdinalIgnoreCase) {
        ".bak", ".tmp", ".temp", ".old", ".orig", ".part", ".crdownload", ".download",
    };


    /// <summary>
    /// True when more than half of the listing's content files are
    /// pictures. <paramref name="companions"/> supplies the sidecar rules —
    /// the same set that folds sidecars into their main row, so the two
    /// cannot disagree about what a sidecar is.
    /// </summary>
    /// <param name="percent">
    /// Share of content files that has to be pictures, in per cent.
    /// Strictly more than this: at exactly the threshold the folder is
    /// split down the middle and there is no obvious right view.
    /// </param>
    public static bool IsImageFolder(
        IReadOnlyList<FileSystemEntry> entries, CompanionResolver companions, int percent = DefaultPercent) {
        int content = 0;
        int images = 0;

        foreach (var entry in entries) {
            if (entry.IsFolderLike || !IsContent(entry.Name, companions)) {
                continue;
            }

            content++;
            if (ImageFormats.IsImage(entry.Name)) {
                images++;
            }
        }

        return images > 0 && images * 100 > content * Math.Clamp(percent, 0, 100);
    }


    private static bool IsContent(string name, CompanionResolver companions) {
        if (name.EndsWith('~')) {
            return false;
        }
        if (_derived.Contains(Path.GetExtension(name))) {
            return false;
        }

        // A sidecar is not content even when its main file is missing: an
        // orphaned .pp3 is still a note about a photograph, not one.
        return companions.RuleFor(name) is null;
    }
}
