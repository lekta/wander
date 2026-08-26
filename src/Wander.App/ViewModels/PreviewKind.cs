namespace Wander.App.ViewModels;

public enum PreviewKind {
    None,
    Image,
    Gif,        // Static images go via Image (BitmapImage); animated GIFs need
                // a frame-by-frame loop, so they get their own view path.
    Text,
    Code,
    Web,        // PDF / HTML / rendered Markdown — anything that goes through WebView2.
    Video,      // .mp4 / .mov / .m4v / etc — rendered via WPF MediaElement.
    Folder,     // A folder (or nothing) is selected: compact census of what is inside.
    Unsupported,
}
