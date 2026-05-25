namespace Wander.Core.FileSystem;

public interface IFileSystem {
    bool DirectoryExists(string path);
    bool FileExists(string path);

    IReadOnlyList<FileSystemEntry> Enumerate(string path);
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
}
