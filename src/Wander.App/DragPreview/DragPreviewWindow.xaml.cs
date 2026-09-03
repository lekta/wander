using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wander.App.Resources;
using Wander.Core.Layout;

namespace Wander.App.DragPreview;

public partial class DragPreviewWindow : Window {
    /// <summary>Gap between the cursor and the plaque, so one never covers the other.</summary>
    private const double CursorGap = 18;

    private static readonly Brush _moveBrush = Palette.DragMove;
    private static readonly Brush _copyBrush = Palette.DragCopy;
    private static readonly Brush _linkBrush = Palette.DragLink;
    private static readonly Brush _forbiddenBrush = Palette.DragForbidden;


    public DragPreviewWindow() {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }


    public void SetIcon(BitmapImage? icon) {
        FileIcon.Source = icon;
    }

    public void SetCount(int total) {
        if (total > 1) {
            CountBadge.Visibility = Visibility.Visible;
            CountLabel.Text = "+" + (total - 1);
        } else {
            CountBadge.Visibility = Visibility.Collapsed;
        }
    }

    public void SetAction(DragAction action, string description, string? target) {
        switch (action) {
            case DragAction.Move:
                ActionIcon.Text = "↪"; // ↪
                ActionIcon.Foreground = _moveBrush;
                break;
            case DragAction.Copy:
                ActionIcon.Text = "＋"; // ＋
                ActionIcon.Foreground = _copyBrush;
                break;
            case DragAction.Link:
                ActionIcon.Text = "↗"; // ↗ (link/shortcut)
                ActionIcon.Foreground = _linkBrush;
                break;
            case DragAction.None:
                // No glyph at all: every one of them is a claim about what
                // would happen, and here nothing would.
                ActionIcon.Text = "";
                break;
            default:
                ActionIcon.Text = "⊘"; // ⊘
                ActionIcon.Foreground = _forbiddenBrush;
                break;
        }
        ActionLabel.Text = description;
        TargetLabel.Text = target ?? "";
        TargetLabel.Visibility = string.IsNullOrEmpty(target) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void MoveToCursor() {
        if (!NativeMethods.GetCursorPos(out var pt)) {
            return;
        }

        // GetCursorPos returns physical pixels. Window.Left/Top is in DIPs.
        // On non-100% DPI scaling the two scales differ; convert via the
        // window's current PresentationSource transform.
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) {
            Left = pt.X + CursorGap;
            Top = pt.Y + CursorGap;

            return;
        }

        var dip = source.CompositionTarget.TransformFromDevice.Transform(new Point(pt.X, pt.Y));
        // Below and to the right of the cursor, and on the other side of it
        // when there is no room — which is what a drag towards the taskbar
        // always runs into. See WindowPlacement.BesideCursor.
        var screen = new ScreenRect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        (Left, Top) = WindowPlacement.BesideCursor(
            dip.X, dip.Y, ActualWidth, ActualHeight, screen, CursorGap);
    }


    private void OnSourceInitialized(object? sender, EventArgs e) {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }
}
