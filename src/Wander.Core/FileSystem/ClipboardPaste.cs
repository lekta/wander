namespace Wander.Core.FileSystem;

/// <summary>What one Ctrl+V takes off the clipboard (PLAN X).</summary>
public enum PasteKind {
    /// <summary>Nothing it can take: files that are nowhere on disk, or formats Wander does not read.</summary>
    None,

    /// <summary>The files on it - copied, or moved when they were cut.</summary>
    Files,

    /// <summary>Its text, as a new <c>.txt</c>.</summary>
    Text,

    /// <summary>Its picture, as a new <c>.png</c>.</summary>
    Image,
}


/// <summary>What a paste takes, and what else was there that it leaves: the journal says so.</summary>
public sealed record PasteChoice(PasteKind Kind, IReadOnlyList<PasteKind> Left);


/// <summary>
/// Which of the clipboard's contents a paste takes (PLAN X). One kind per
/// paste, in this order: files, then text, then a picture (decision of
/// 2026-09-24) - Excel puts the text of the cells and a picture of them
/// there together, and the text is what the cells hold.
/// </summary>
public static class ClipboardPaste {
    /// <summary>
    /// The kind to take out of what the clipboard held when last read.
    /// Files that exist nowhere on disk - an attachment, an entry of a zip
    /// open in Explorer - are files all the same: nothing is pasted in their
    /// place, and the text beside them (their names, as a rule) is not what
    /// was copied.
    /// </summary>
    public static PasteChoice Choose(ClipboardFiles content) {
        if (content.HasContent) {
            return new PasteChoice(PasteKind.Files, Left(content.HasText, content.HasImage));
        }
        if (content.HasUnsupportedFiles) {
            return new PasteChoice(PasteKind.None, Array.Empty<PasteKind>());
        }
        if (content.HasText) {
            return new PasteChoice(PasteKind.Text, Left(text: false, content.HasImage));
        }

        return new PasteChoice(content.HasImage ? PasteKind.Image : PasteKind.None, Array.Empty<PasteKind>());
    }


    private static IReadOnlyList<PasteKind> Left(bool text, bool image) {
        var left = new List<PasteKind>(2);
        if (text) {
            left.Add(PasteKind.Text);
        }
        if (image) {
            left.Add(PasteKind.Image);
        }

        return left;
    }
}
