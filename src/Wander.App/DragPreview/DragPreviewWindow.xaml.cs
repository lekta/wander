using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wander.App.DragPreview;

public partial class DragPreviewWindow : Window {
    private static readonly Brush _moveBrush = Brushes.SteelBlue;
    private static readonly Brush _copyBrush = Brushes.SeaGreen;
    private static readonly Brush _forbiddenBrush = Brushes.IndianRed;


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
        if (NativeMethods.GetCursorPos(out var pt)) {
            // Offset so the preview doesn't cover the cursor / system drop cursor.
            Left = pt.X + 18;
            Top = pt.Y + 18;
        }
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
