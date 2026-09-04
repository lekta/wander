using Wander.Core.FileSystem;

namespace Wander.Core.Tests.Fakes;

// Not sealed: FolderStatisticsTests needs a variant that refuses to list one
// particular folder, and overriding Enumerate is cheaper than a second fake.
internal class FakeFileSystem : IFileSystem {
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CallLog { get; } = new();


    public bool DirectoryExists(string path) {
        return Directories.Contains(path);
    }

    public bool FileExists(string path) {
        return Files.ContainsKey(path);
    }


    public virtual IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null) {
        var options = sort ?? SortOptions.Default;

        var folderLikes = new List<FileSystemEntry>();
        var files = new List<FileSystemEntry>();

        foreach (string d in Directories) {
            string? parent = System.IO.Path.GetDirectoryName(d);
            if (string.Equals(parent, path, StringComparison.OrdinalIgnoreCase)) {
                folderLikes.Add(new FileSystemEntry(System.IO.Path.GetFileName(d), d, EntryKind.Directory, null, DateTime.MinValue, false, false, false, false));
            }
        }

        foreach (string f in Files.Keys) {
            string? parent = System.IO.Path.GetDirectoryName(f);
            if (string.Equals(parent, path, StringComparison.OrdinalIgnoreCase)) {
                files.Add(new FileSystemEntry(System.IO.Path.GetFileName(f), f, EntryKind.File, Files[f].Length, DateTime.MinValue, false, false, false, false));
            }
        }

        var all = new List<FileSystemEntry>(folderLikes.Count + files.Count);
        all.AddRange(folderLikes);
        all.AddRange(files);

        return EntryComparers.Sort(all, options);
    }

    public IReadOnlyList<FileSystemEntry> GetRoots() {
        return Array.Empty<FileSystemEntry>();
    }

    public FileSystemEntry? GetEntry(string path) {
        if (Directories.Contains(path)) {
            return new FileSystemEntry(System.IO.Path.GetFileName(path), path, EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);
        }
        if (Files.TryGetValue(path, out var bytes)) {
            return new FileSystemEntry(System.IO.Path.GetFileName(path), path, EntryKind.File, bytes.Length, DateTime.MinValue, false, false, false, false);
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

    public byte[] ReadAllBytes(string path) {
        return Files.TryGetValue(path, out byte[]? data)
            ? data
            : throw new System.IO.FileNotFoundException("Not found", path);
    }

    public System.IO.Stream OpenRead(string path) {
        return new System.IO.MemoryStream(ReadAllBytes(path), writable: false);
    }

    public void ReplaceAtomic(string path, byte[] content) {
        CallLog.Add($"ReplaceAtomic:{path}");
        Files[path] = content;
    }

    public void ClearReadOnly(string path) {
        CallLog.Add($"ClearReadOnly:{path}");
    }

    /// <summary>
    /// How much of a file goes out per progress report. Small on purpose:
    /// the real copy reports many times inside one file, and a test that
    /// wants to see a bar move part-way through has to be able to.
    /// </summary>
    public int CopyChunk { get; set; } = 4;


    public void CopyFile(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default) {
        CallLog.Add($"CopyFile:{source}->{destination}:{overwrite}");
        ct.ThrowIfCancellationRequested();

        byte[] data = Files[source];
        Files[destination] = data;
        Report(data.Length, bytesCopied, ct);
    }

    public void CopyDirectory(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default) {
        CallLog.Add($"CopyDirectory:{source}->{destination}:{overwrite}");
        ct.ThrowIfCancellationRequested();
        Directories.Add(destination);
    }

    public void MoveEntry(string source, string destination,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default) {
        CallLog.Add($"MoveEntry:{source}->{destination}");
        ct.ThrowIfCancellationRequested();

        if (Files.TryGetValue(source, out byte[]? data)) {
            Files.Remove(source);
            Files[destination] = data;
        }

        if (Directories.Remove(source)) {
            Directories.Add(destination);
        }
    }

    /// <summary>Paths whose rename must fail — for exercising rollback paths.</summary>
    public HashSet<string> RenameFailures { get; } = new(StringComparer.OrdinalIgnoreCase);


    public void Rename(string path, string newName) {
        CallLog.Add($"Rename:{path}->{newName}");
        if (RenameFailures.Contains(path)) {
            throw new System.IO.IOException($"Rename refused: {path}");
        }
        string parent = System.IO.Path.GetDirectoryName(path)!;
        MoveEntry(path, System.IO.Path.Combine(parent, newName));
    }


    /// <summary>Hands out <paramref name="length"/> bytes a chunk at a time.</summary>
    private void Report(int length, IProgress<long>? bytesCopied, CancellationToken ct) {
        if (bytesCopied is null) {
            return;
        }

        int chunk = Math.Max(1, CopyChunk);
        for (int sent = 0; sent < length; sent += chunk) {
            ct.ThrowIfCancellationRequested();
            bytesCopied.Report(Math.Min(chunk, length - sent));
        }
    }
}
