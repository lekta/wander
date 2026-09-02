using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wander.Harness.Host;

/// <summary>
/// Renders the window's visual tree to a PNG. This is a software render of
/// the tree, not a screen capture, so it works with the window parked
/// off-screen; the one thing it cannot show is HWND-hosted content
/// (WebView2), which comes out blank.
/// </summary>
public static class Screenshot {
    public static string Save(Window window, string path) {
        var root = (FrameworkElement)window.Content;
        int width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);

        return path;
    }
}
