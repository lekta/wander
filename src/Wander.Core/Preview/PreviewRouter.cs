using Wander.Core.Icons;

namespace Wander.Core.Preview;

/// <summary>
/// Which loader a file goes to. Not the same question as which control
/// draws the result: Markdown, FB2 and PDF all end up in the web view but
/// arrive there three different ways, and a Unity asset is read as code or
/// refused depending on what its first bytes turn out to be.
/// </summary>
public enum PreviewRoute {
    /// <summary>Nothing here can be shown.</summary>
    Unsupported,

    /// <summary>A <c>.lnk</c> — the preview is of whatever it points at.</summary>
    Shortcut,

    /// <summary>Multi-frame image: composited frame by frame rather than decoded once.</summary>
    Animation,
    Video,
    Audio,
    Model,
    Image,

    /// <summary>Handed to the web view by path — PDF, HTML, MHTML.</summary>
    Web,

    /// <summary>FictionBook: parsed here into HTML, then shown in the web view.</summary>
    Book,

    /// <summary>Rich text — read natively by the view into a flow document.</summary>
    Document,

    /// <summary>Rendered to HTML, then shown in the web view.</summary>
    Markdown,

    /// <summary>Text with syntax highlighting.</summary>
    Code,

    /// <summary>
    /// Text in one project and an opaque blob in the next; the bytes decide
    /// (see <see cref="TextProbe"/>), and the file is then read as code.
    /// </summary>
    MaybeText,

    /// <summary>Plain text, no highlighting.</summary>
    Text,
}


/// <summary>
/// The extension → route table of the preview pane. Pure lookup, no
/// decoding and no file access: routing is the part worth a test, and it
/// answers before a byte of the file has been read.
///
/// <para>
/// Order is the whole content of this table, not an implementation
/// detail — most of these lists overlap. A <c>.webp</c> is a picture and
/// an animation, a <c>.svg</c> is a picture and a source file, a
/// <c>.mtl</c> sits next to models and is text. The first matching rule
/// wins, and moving one rule past another changes what the pane shows.
/// </para>
/// </summary>
public static class PreviewRouter {
    /// <summary>
    /// Animated containers go through a frame compositor rather than a
    /// one-shot decode. WEBP files are usually static, but the codec can
    /// surface multiple frames for animated ones, and a compositor handed a
    /// single frame just shows that frame — so routing every <c>.webp</c>
    /// here costs static files nothing and unlocks playback for the rest.
    /// </summary>
    private static readonly HashSet<string> _animation = new(StringComparer.OrdinalIgnoreCase) {
        ".gif", ".webp",
    };

    /// <summary>
    /// What Windows Media Foundation plays out of the box on Win10/11.
    /// MKV / WEBM are listed but fall back to "unsupported" when the user
    /// has no extension packs installed.
    /// </summary>
    private static readonly HashSet<string> _video = new(StringComparer.OrdinalIgnoreCase) {
        ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm",
    };

    private static readonly HashSet<string> _text = new(StringComparer.OrdinalIgnoreCase) {
        ".txt", ".log", ".csv", ".tsv",
        ".ini", ".cfg", ".conf", ".toml", ".env", ".gitignore", ".gitattributes",
        ".editorconfig",
        // A .mtl is not a model, it is the short text file that names a
        // model's materials and their texture maps — and reading it is
        // usually the reason anyone opens one.
        ".mtl",
    };

    private static readonly HashSet<string> _code = new(StringComparer.OrdinalIgnoreCase) {
        ".cs", ".csproj", ".props", ".targets", ".sln", ".slnx",
        ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs",
        ".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift", ".php",
        ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".m", ".mm",
        ".css", ".scss", ".less",
        ".sh", ".bash", ".zsh", ".ps1", ".bat", ".cmd",
        ".sql",
        ".xml", ".xaml", ".svg",
        ".json", ".yaml", ".yml",
        // Patches — the editor's own "Patch" definition colours these, so
        // routing them here is the whole feature.
        ".diff", ".patch",
        // Unity shaders and their includes.
        ".shader", ".cginc", ".hlsl", ".compute",
    };

    /// <summary>
    /// Unity's serialised assets — YAML only when the project forces text
    /// serialization, and a binary blob otherwise.
    /// </summary>
    private static readonly HashSet<string> _maybeText = new(StringComparer.OrdinalIgnoreCase) {
        ".asset", ".prefab", ".unity", ".mat",
    };

    /// <summary>
    /// Handed straight to the web view by path. PDF and HTML have always
    /// been here; MHTML joins them because Chromium reads the format
    /// natively, which is the whole reason a saved web page can be
    /// previewed at all.
    /// </summary>
    private static readonly HashSet<string> _web = new(StringComparer.OrdinalIgnoreCase) {
        ".pdf", ".html", ".htm", ".mht", ".mhtml",
    };


    /// <summary>The route for a path, decided by its extension alone.</summary>
    public static PreviewRoute Route(string path) {
        return ForExtension(Path.GetExtension(path));
    }


    /// <summary>
    /// The route for an extension (with its leading dot, or empty for a
    /// file that has none).
    /// </summary>
    public static PreviewRoute ForExtension(string extension) {
        string ext = extension ?? "";

        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return PreviewRoute.Shortcut;
        }
        if (_animation.Contains(ext)) {
            return PreviewRoute.Animation;
        }
        if (_video.Contains(ext)) {
            return PreviewRoute.Video;
        }
        if (AudioTags.Extensions.Contains(ext)) {
            return PreviewRoute.Audio;
        }
        if (MeshFile.Extensions.Contains(ext)) {
            return PreviewRoute.Model;
        }
        if (ImageFormats.All.Contains(ext)) {
            return PreviewRoute.Image;
        }
        if (_web.Contains(ext)) {
            return PreviewRoute.Web;
        }
        if (ext.Equals(".fb2", StringComparison.OrdinalIgnoreCase)) {
            return PreviewRoute.Book;
        }
        if (ext.Equals(".rtf", StringComparison.OrdinalIgnoreCase)) {
            return PreviewRoute.Document;
        }
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)) {
            return PreviewRoute.Markdown;
        }
        if (_code.Contains(ext)) {
            return PreviewRoute.Code;
        }
        if (_maybeText.Contains(ext)) {
            return PreviewRoute.MaybeText;
        }

        // A file with no extension is read as text: that is what a README,
        // a LICENSE or a dotfile-less config turns out to be often enough
        // that refusing them would be the wrong default.
        if (_text.Contains(ext) || ext.Length == 0) {
            return PreviewRoute.Text;
        }

        return PreviewRoute.Unsupported;
    }
}
