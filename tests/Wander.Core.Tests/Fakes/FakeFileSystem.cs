using Wander.Core.FileSystem;

namespace Wander.Core.Tests.Fakes;

internal sealed class FakeFileSystem : IFileSystem {
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CallLog { get; } = new();


    public bool DirectoryExists(string path) {
        return Directories.Contains(path);
    }

    public bool FileExists(string path) {
        return Files.ContainsKey(path);
    }


    public IReadOnlyList<FileSystemEntry> Enumerate(string path) {
        var entries = new List<FileSystemEntry>();

        foreach (string d in Directories) {
            string? parent = System.IO.Path.GetDirectoryName(d);
            if (string.Equals(parent, path, StringComparison.OrdinalIgnoreCase)) {
                entries.Add(new FileSystemEntry(System.IO.Path.GetFileName(d), d, EntryKind.Directory, null, DateTime.MinValue, false, false, false));
            }
        }

        foreach (string f in Files.Keys) {
            string? parent = System.IO.Path.GetDirectoryName(f);
            if (string.Equals(parent, path, StringComparison.OrdinalIgnoreCase)) {
                entries.Add(new FileSystemEntry(System.IO.Path.GetFileName(f), f, EntryKind.File, Files[f].Length, DateTime.MinValue, false, false, false));
            }
        }

        return entries;
    }

    public IReadOnlyList<FileSystemEntry> GetRoots() {
        return Array.Empty<FileSystemEntry>();
    }

    public FileSystemEntry? GetEntry(string path) {
        if (Directories.Contains(path)) {
            return new FileSystemEntry(System.IO.Path.GetFileName(path), path, EntryKind.Directory, null, DateTime.MinValue, false, false, false);
        }
        if (Files.TryGetValue(path, out var bytes)) {
            return new FileSystemEntry(System.IO.Path.GetFileName(path), path, EntryKind.File, bytes.Length, DateTime.MinValue, false, false, false);
        }
        return null;
    }

    public bool HasSubdirectories(string path) {
        foreach (string d in Directories) {
            string? parent = System.IO.Path.GetDirectoryName(d);
            if (string.Equals(parent, path, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    public string? GetParent(string path) {
        return System.IO.Path.GetDirectoryName(path);
    }


    public void CreateDirectory(string path) {
        CallLog.Add($"CreateDirectory:{path}");
        Directories.Add(path);
    }

    public void DeleteFile(string path) {
        CallLog.Add($"DeleteFile:{path}");
        Files.Remove(path);
    }

    public void DeleteDirectory(string path, bool recursive) {
        CallLog.Add($"DeleteDirectory:{path}:{recursive}");
        Directories.Remove(path);
    }

    public void ClearReadOnly(string path) {
        CallLog.Add($"ClearReadOnly:{path}");
    }

    public void CopyFile(string source, string destination, bool overwrite) {
        CallLog.Add($"CopyFile:{source}->{destination}:{overwrite}");
        Files[destination] = Files[source];
    }

    public void CopyDirectory(string source, string destination, bool overwrite) {
        CallLog.Add($"CopyDirectory:{source}->{destination}:{overwrite}");
        Directories.Add(destination);
    }

    public void MoveEntry(string source, string destination) {
        CallLog.Add($"MoveEntry:{source}->{destination}");

        if (Files.TryGetValue(source, out byte[]? data)) {
            Files.Remove(source);
            Files[destination] = data;
        }

        if (Directories.Remove(source)) {
            Directories.Add(destination);
        }
    }

    public void Rename(string path, string newName) {
        CallLog.Add($"Rename:{path}->{newName}");
        string parent = System.IO.Path.GetDirectoryName(path)!;
        MoveEntry(path, System.IO.Path.Combine(parent, newName));
    }
}
