using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Localization;

namespace Wander.Core.Actions;

/// <summary>
/// How several items are named where they are counted - the header's
/// "Actions", the drop menu, the drag plaque, the questions before a delete
/// or a move: as narrowly as all of them allow (2026-10-07). One extension -
/// "3 CR3"; RAW of several makers - "3 RAW"; pictures - "4 изображения"; one
/// of the other groups - its noun ("2 видео"); files of any kind - "5
/// файлов"; folders - "2 папки"; anything else, folders and files together -
/// "7 элементов".
/// </summary>
public static class CountCaption {
    public const string ItemsKey = "MenuCaptionSelection";
    public const string FilesKey = "MenuCaptionFiles";
    public const string FoldersKey = "MenuCaptionFolders";
    public const string RawKey = "MenuCaptionRaw";
    public const string ExtensionKey = "MenuCaptionExtension";


    /// <summary>
    /// "3 CR3", "4 изображения", "7 элементов" - <paramref name="items"/>
    /// counted under the narrowest name they share (<see cref="ShapeOf"/>).
    /// </summary>
    public static string Of(IReadOnlyList<FileSystemEntry> items, ITextSource? text = null) {
        var (key, extension) = ShapeOf(items);
        string forms = text is null ? Text.Get(key) : text.Get(key);
        if (extension is null) {
            return Text.PluralForm(forms, items.Count);
        }

        try {
            return string.Format(forms, items.Count, extension);
        } catch (FormatException) {
            return forms;
        }
    }


    /// <summary>"3 элемента": items known only by their paths - what they are is not asked of the disk for a caption.</summary>
    public static string Items(int count) {
        return Text.Plural(ItemsKey, count);
    }


    /// <summary>
    /// The resource key the caption is written by, and the extension it
    /// names - upper case, without the dot - when every item is a file with
    /// that one extension. Tested by shape: the words are the resx's.
    /// </summary>
    public static (string Key, string? Extension) ShapeOf(IReadOnlyList<FileSystemEntry> items) {
        if (items.Count == 0 || !items.All(e => e.Kind == EntryKind.File)) {
            return (items.Count > 0 && items.All(e => e.Kind == EntryKind.Directory) ? FoldersKey : ItemsKey, null);
        }

        string first = Path.GetExtension(items[0].Name);
        if (first.Length > 1 && items.All(e => string.Equals(Path.GetExtension(e.Name), first, StringComparison.OrdinalIgnoreCase))) {
            return (ExtensionKey, first[1..].ToUpperInvariant());
        }
        if (items.All(e => ImageFormats.IsRaw(e.Name))) {
            return (RawKey, null);
        }

        return (FileTypeGroups.Classify(items) is { } group ? GroupKey(group) : FilesKey, null);
    }


    private static string GroupKey(FileTypeGroup group) {
        return group switch {
            FileTypeGroup.Images => "MenuCaptionImages",
            FileTypeGroup.Video => "MenuCaptionVideo",
            FileTypeGroup.Audio => "MenuCaptionAudio",
            FileTypeGroup.TextAndCode => "MenuCaptionTextAndCode",
            FileTypeGroup.Documents => "MenuCaptionDocuments",
            FileTypeGroup.Archives => "MenuCaptionArchives",
            FileTypeGroup.Folders => FoldersKey,
            _ => FilesKey,
        };
    }
}
