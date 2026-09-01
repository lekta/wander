using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Wander.App.Views;

/// <summary>
/// The search criteria, in a window of their own.
///
/// <para>
/// One instance per main window, created on first use and hidden rather
/// than destroyed on close — reopening it should find the last query still
/// in it, and the criteria live in the view model anyway, so a window that
/// rebuilt itself would only be slower.
/// </para>
/// </summary>
public partial class SearchWindow : Window {
    public SearchWindow() {
        InitializeComponent();
    }


    /// <summary>
    /// Raised when the window goes away, so the caller can put the keyboard
    /// back in the file list. Losing focus into nowhere after Esc is the
    /// exact complaint this answers: when a window closes, the keyboard has
    /// to land somewhere the user can act.
    /// </summary>
    public event EventHandler? Dismissed;


    private MainViewModel Vm => (MainViewModel)DataContext;


    /// <summary>
    /// Brings the window up next to its owner and puts the keyboard in the
    /// name field, whether it was closed or merely behind something.
    /// </summary>
    public void ShowAndFocus() {
        if (!IsVisible) {
            Show();
        }
        if (WindowState == WindowState.Minimized) {
            WindowState = WindowState.Normal;
        }

        Activate();
        NameBox.Focus();
        NameBox.SelectAll();
    }


    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) {
        base.OnClosing(e);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }


    // --- Window style interop -------------------------------------------


    /// <summary>
    /// Strips the minimise and maximise boxes, leaving a resizable frame
    /// with nothing in the corner but the close button.
    ///
    /// <para>
    /// WPF offers no style with that combination: <c>ToolWindow</c> has no
    /// minimise or maximise either, but it comes with the short caption
    /// whose close button fills a fraction of the corner and reads as a
    /// small red square rather than the button every other window has.
    /// <c>NoResize</c> hides them too, at the cost of the resizing this
    /// window needs. Two lines of window style do what the enum cannot.
    /// </para>
    /// </summary>
    private void Window_SourceInitialized(object? sender, EventArgs e) {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) {
            return;
        }

        int style = GetWindowLong(handle, GwlStyle);
        SetWindowLong(handle, GwlStyle, style & ~WsMinimizeBox & ~WsMaximizeBox);
    }


    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
        // Esc closes the window and leaves whatever is on screen alone;
        // clearing is a button, because a key that both closed the window
        // and threw away the results would be one keystroke doing two
        // things, only one of which was asked for.
        if (e.Key == Key.Escape) {
            Close();
            e.Handled = true;

            return;
        }

        // Enter runs it now, ahead of the pause and of the length floor.
        // The keyboard stays where it is so the query can be corrected.
        if (e.Key is Key.Enter or Key.Return) {
            Vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }


    private const int GwlStyle = -16;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;


    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);


    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}
