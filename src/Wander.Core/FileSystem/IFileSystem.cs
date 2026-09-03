namespace Wander.Core.FileSystem;

public interface IFileSystem {
    bool DirectoryExists(string path);
    bool FileExists(string path);

    /// <summary>
    /// List directory contents. <paramref name="sort"/> picks the column /
    /// direction / folder-grouping; passing <c>null</c> uses
    /// <see cref="SortOptions.Default"/> (name A→Z, folders first) — the
    /// sane default for callers that don't expose sort to the user
    /// (tree view, tests, ad-hoc enumerations).
    /// </summary>
    IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null);
    IReadOnlyList<FileSystemEntry> GetRoots();

    /// <summary>Returns metadata for a single path or null if it doesn't exist.</summary>
    FileSystemEntry? GetEntry(string path);

    /// <summary>
    /// Cheap probe: does <paramref name="path"/> contain at least one subdirectory?
    /// Used by the tree view to decide whether to draw an expand chevron without
    /// loading the full content of the directory.
    /// </summary>
    bool HasSubdirectories(string path);

    string? GetParent(string path);

    void CreateDirectory(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);

    /// <summary>Clear the read-only attribute on a file or folder, so it can be deleted/modified.</summary>
    void ClearReadOnly(string path);
    void CopyFile(string source, string destination, bool overwrite);
    void CopyDirectory(string source, string destination, bool overwrite);
    void MoveEntry(string source, string destination);
    void Rename(string path, string newName);

    /// <summary>
    /// Raw bytes of a small file. Bytes rather than text on purpose: the
    /// companion-sidecar path has to round-trip the original encoding and
    /// BOM, so decoding is the caller's decision, not the layer's.
    /// </summary>
    byte[] ReadAllBytes(string path);

    /// <summary>
    /// The file's bytes as a stream, for the one reader that must not load
    /// the whole thing: "are these two the same?" over files of any size
    /// (<see cref="FileContentComparer"/>). Shared read access - the file
    /// may be open in an editor at the time.
    /// </summary>
    Stream OpenRead(string path);

    /// <summary>
    /// Replace a file's content without ever leaving it half-written: the
    /// new content goes to a temporary file next to the target, which is
    /// then swapped in. Used for edits to third-party formats, where a
    /// truncated file means somebody's lost work.
    /// </summary>
    void ReplaceAtomic(string path, byte[] content);
}
