namespace Wander.App.ViewModels;

public enum PreviewKind {
    None,
    Image,
    Gif,        // Static images go via Image (BitmapImage); animated GIFs need
                // a frame-by-frame loop, so they get their own view path.
    Text,
    Code,
    Web,        // PDF / HTML / MHTML / rendered Markdown and FB2 — anything that goes through WebView2.
    Document,   // .rtf — WPF reads it natively into a FlowDocument, so it gets a RichTextBox.
    Video,      // .mp4 / .mov / .m4v / etc — rendered via WPF MediaElement.
    Audio,      // .mp3 / .flac — the same MediaElement and transport, with the track's tags instead of a picture.
    Model,      // .stl / .obj / .gltf / .glb — geometry parsed in Core, drawn in a WPF Viewport3D.
    Folder,     // A folder (or nothing) is selected: compact census of what is inside.
    Unsupported,
}
