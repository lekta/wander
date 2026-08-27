using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Wander.App.Highlighting;

/// <summary>
/// The syntax definitions Wander adds to the ones AvalonEdit ships with.
///
/// <para>
/// AvalonEdit covers the mainstream languages (C#, C++, JS, Python, XML,
/// JSON, Markdown, Patch, …) and answers by extension, so most of the code
/// preview needs nothing from us. These are the gaps that showed up in real
/// folders: build scripts on both sides (<c>.bat</c> / <c>.cmd</c> and
/// <c>.sh</c> — AvalonEdit ships PowerShell but not sh), Unity shaders, and
/// everything YAML, which includes Unity's own serialised assets.
/// </para>
///
/// <para>
/// Registration is a process-wide side effect on
/// <see cref="HighlightingManager.Instance"/>, so it happens once and is
/// idempotent; the preview pane calls it before it first asks for a
/// definition.
/// </para>
/// </summary>
public static class HighlightingCatalog {
    private static bool _registered;
    private static readonly object _lock = new();


    public static void EnsureRegistered() {
        lock (_lock) {
            if (_registered) {
                return;
            }
            _registered = true;

            Register("Batch", "Batch.xshd", ".bat", ".cmd");
            Register("Shell", "Shell.xshd", ".sh", ".bash", ".zsh", ".ksh", ".bashrc", ".profile");
            Register("ShaderLab", "ShaderLab.xshd", ".shader", ".cginc", ".hlsl", ".compute");
            Register("YAML", "Yaml.xshd", ".yaml", ".yml", ".asset", ".prefab", ".unity", ".mat", ".meta");
        }
    }


    /// <summary>
    /// Loads one definition out of the assembly and files it under the
    /// extensions it should answer to. A definition that fails to parse is
    /// skipped rather than thrown: a broken colour scheme must not be the
    /// reason the preview pane refuses to open a file.
    /// </summary>
    private static void Register(string name, string resource, params string[] extensions) {
        try {
            using Stream? stream = typeof(HighlightingCatalog).Assembly
                .GetManifestResourceStream("Wander.App.Highlighting." + resource);
            if (stream is null) {
                return;
            }

            using var reader = XmlReader.Create(stream);
            var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting(name, extensions, definition);
        } catch (Exception ex) when (ex is XmlException or HighlightingDefinitionInvalidException) {
            // Malformed .xshd — the affected files simply stay unhighlighted.
        }
    }
}
