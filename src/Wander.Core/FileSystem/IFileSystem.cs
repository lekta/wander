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

    /// <summary>
    /// Copy one file. <paramref name="bytesCopied"/> is told the delta after
    /// each chunk, so a single large file has a bar that moves; passing null
    /// asks for the plain copy. <paramref name="ct"/> stops it part-way and
    /// leaves no half-written tail behind.
    /// </summary>
    void CopyFile(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default);

    /// <summary>Copy a folder recursively, file by file - see <see cref="CopyFile"/> for the two extra arguments.</summary>
    void CopyDirectory(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default);

    /// <summary>
    /// Move a file or folder. Within one volume this is a rename and the two
    /// extra arguments never come into play; across volumes it is a copy
    /// followed by a delete, and they behave as in <see cref="CopyFile"/>.
    /// </summary>
    void MoveEntry(string source, string destination,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default);

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
