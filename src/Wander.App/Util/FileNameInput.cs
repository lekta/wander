using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Wander.App.Util;

/// <summary>
/// Text pasted or dropped into an inline name editor: the characters Windows
/// will not take in a name are dropped from it, the way typing refuses them
/// (PreviewTextInput, which a paste or a drop never raises). The operation
/// refuses a name that is no name anyway (FileOperationService.Rename); this
/// keeps "..\a.txt" from getting that far.
/// </summary>
internal static class FileNameInput {
    private static readonly char[] _invalid = Path.GetInvalidFileNameChars();


    /// <param name="applies">Whether the box edits a file's name now - a bookmark's is no file's.</param>
    /// <param name="dropped">Something was taken out of the text.</param>
    public static void FilterPastes(TextBox box, Func<bool> applies, Action dropped) {
        DataObject.AddPastingHandler(box, (_, e) => {
            if (!applies() || e.DataObject.GetData(DataFormats.UnicodeText, autoConvert: true) is not string text) {
                return;
            }

            string clean = string.Concat(text.Where(c => Array.IndexOf(_invalid, c) < 0));
            if (clean.Length == text.Length) {
                return;
            }

            dropped();
            if (clean.Length == 0) {
                e.CancelCommand();

                return;
            }

            e.DataObject = new DataObject(DataFormats.UnicodeText, clean);
            e.FormatToApply = DataFormats.UnicodeText;
        });
    }
}
