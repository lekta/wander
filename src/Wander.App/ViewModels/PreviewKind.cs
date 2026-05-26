namespace Wander.App.ViewModels;

public enum PreviewKind {
    None,
    Image,
    Text,
    Code,
    Web,        // PDF / HTML / rendered Markdown — anything that goes through WebView2.
    Unsupported,
}
