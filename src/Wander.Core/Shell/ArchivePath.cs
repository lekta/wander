namespace Wander.Core.Shell;

/// <summary>
/// A path that points inside an archive, split in two:
/// <c>D:\packs\photos.zip\raw\IMG.CR2</c> becomes archive
/// <c>D:\packs\photos.zip</c> and inner <c>raw\IMG.CR2</c>.
///
/// <para>
/// Windows itself has no such split - to the shell the whole string is one
/// parsing name and that is exactly how it is handed back to
/// <c>SHCreateItemFromParsingName</c>. Wander needs the two halves for its
/// own questions: which file on disk is the container (does it still exist,
/// what does the tree highlight, what does the status bar name), and what
/// sits inside it (which entry is being extracted, whether we are at the
/// archive's root).
/// </para>
/// </summary>
/// <param name="Archive">Path of the archive file itself, on disk.</param>
/// <param name="Inner">Path inside it, "" at the archive's root.</param>
public sealed record ArchivePath(string Archive, string Inner) {
    /// <summary>True when the path names the archive itself, not an entry in it.</summary>
    public bool IsRoot => Inner.Length == 0;

    /// <summary>Name of the archive file, for status text and titles.</summary>
    public string ArchiveName => Path.GetFileName(Archive);


    /// <summary>
    /// Splits <paramref name="path"/> at its first archive segment, or
    /// returns null when it has none. Pure string work - whether the
    /// archive exists on disk is the platform layer's question, and the
    /// only one that can tell a real folder named <c>x.zip</c> from an
    /// archive.
    /// </summary>
    /// <param name="extensions">
    /// Archive extensions, dot included and <b>lower case</b>
    /// ({".zip", ".7z", ".gz", ...}) - the comparison lower-cases the
    /// segment it tests, so the set's own comparer does not matter.
    /// </param>
    public static ArchivePath? Parse(string? path, IReadOnlySet<string> extensions) {
        if (string.IsNullOrEmpty(path) || extensions.Count == 0) {
            return null;
        }

        // Walked by hand rather than Split: the archive half has to come
        // back as the caller wrote it, separators and UNC root included.
        int start = 0;
        for (int i = 0; i <= path.Length; i++) {
            if (i < path.Length && path[i] != '\\' && path[i] != '/') {
                continue;
            }

            if (i > start && extensions.Contains(Path.GetExtension(path[start..i]).ToLowerInvariant())) {
                string inner = i < path.Length
                    ? path[(i + 1)..].Trim('\\', '/')
                    : "";

                return new ArchivePath(path[..i], inner);
            }
            start = i + 1;
        }

        return null;
    }
}
