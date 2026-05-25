namespace Wander.Core.FileSystem;

public interface IFileSystem {
    bool DirectoryExists(string path);
    bool FileExists(string path);

    IReadOnlyList<FileSystemEntry> Enumerate(string path);
    IReadOnlyList<FileSystemEntry> GetRoots();

    string? GetParent(string path);

    void CreateDirectory(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    void CopyFile(string source, string destination, bool overwrite);
    void CopyDirectory(string source, string destination, bool overwrite);
    void MoveEntry(string source, string destination);
    void Rename(string path, string newName);
}
