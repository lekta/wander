using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace Wander.App.Util;

/// <summary>
/// The window's Ctrl+C - copy the files - stepping aside for a web view.
/// WebView2 hands the browser's accelerator keys to WPF first, as key
/// events raised on itself, and lets the browser act only when nobody
/// handled them; an input binding always marks its key handled. So a
/// window-wide Ctrl+C took the copy away from text selected in a table, a
/// page or a PDF of the preview and copied the file instead. A key out of
/// a web view does not match, and the browser copies what is selected in
/// it.
/// </summary>
public sealed class FileCopyGesture : KeyGesture {
    public FileCopyGesture() : base(Key.C, ModifierKeys.Control) {
    }


    public override bool Matches(object targetElement, InputEventArgs inputEventArgs) {
        return inputEventArgs.OriginalSource is not WebView2 && base.Matches(targetElement, inputEventArgs);
    }
}
