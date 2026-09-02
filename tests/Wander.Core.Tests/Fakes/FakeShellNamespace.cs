using Wander.Core.FileSystem;
using Wander.Core.Shell;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// An in-memory archive: a dictionary of "path inside" to contents, and a
/// <see cref="FakeFileSystem"/> that <see cref="CopyOut"/> writes into.
/// Enough to test extraction end to end - conflicts, undo, cancellation -
/// without a real zip or a real shell.
/// </summary>
internal sealed class FakeShellNamespace : IShellNamespace {
    private readonly FakeFileSystem _fs;
    private readonly HashSet<string> _extensions;

    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);


    public FakeShellNamespace(FakeFileSystem fs, params string[] extensions) {
        _fs = fs;
        _extensions = new HashSet<string>(
            extensions.Length > 0 ? extensions : new[] { ".zip" },
            StringComparer.OrdinalIgnoreCase);
    }


    /// <summary>Paths handed to the last <see cref="CopyOut"/>, in order.</summary>
    public List<CopyOutItem> CopiedOut { get; } = new();

    /// <summary>Set to make <see cref="CopyOut"/> throw - a broken or locked archive.</summary>
    public Exception? CopyOutFailure { get; set; }


    /// <summary>Adds a file inside an archive: full path, then its contents.</summary>
    public FakeShellNamespace AddFile(string fullPath, string content = "x") {
        _files[fullPath] = content;
        AddParents(fullPath);

        return this;
    }

    /// <summary>Adds an empty folder inside an archive.</summary>
    public FakeShellNamespace AddFolder(string fullPath) {
        _folders.Add(fullPath);
        AddParents(fullPath);

        return this;
    }


    public bool IsShellPath(string path) {
        return ParseArchive(path) is not null;
    }

    public ArchivePath? ParseArchive(string path) {
        return ArchivePath.Parse(path, _extensions);
    }

    public bool CanNavigate(string path) {
        return _folders.Contains(path);
    }

    public string? GetDisplayName(string shellPath) {
        return null;
    }

    public IReadOnlyList<FileSystemEntry> Enumerate(string shellPath) {
        var children = new List<FileSystemEntry>();
        foreach (string folder in _folders) {
            if (IsChildOf(folder, shellPath)) {
                children.Add(Entry(folder, EntryKind.Directory, size: null));
            }
        }
        foreach (var (file, content) in _files) {
            if (IsChildOf(file, shellPath)) {
                children.Add(Entry(file, EntryKind.File, content.Length));
            }
        }

        return children;
    }

    public Task CopyOut(
        IReadOnlyList<CopyOutItem> items, string targetFolder,
        IProgress<string>? progress, CancellationToken ct) {

        if (CopyOutFailure is not null) {
            throw CopyOutFailure;
        }

        foreach (var item in items) {
            ct.ThrowIfCancellationRequested();
            CopiedOut.Add(item);
            string name = item.NewName ?? Path.GetFileName(item.Path);
            Write(item.Path, Path.Combine(targetFolder, name));
            progress?.Report(item.Path);
        }

        return Task.CompletedTask;
    }


    private void Write(string source, string destination) {
        if (_files.TryGetValue(source, out string? content)) {
            _fs.Files[destination] = System.Text.Encoding.UTF8.GetBytes(content);

            return;
        }

        _fs.Directories.Add(destination);
        foreach (var child in Enumerate(source)) {
            Write(child.FullPath, Path.Combine(destination, child.Name));
        }
    }

    private void AddParents(string fullPath) {
        // Everything between the archive and the entry is a folder, so a
        // test can add "pack.zip\a\b.txt" and get "a" for free.
        var archive = ParseArchive(fullPath);
        if (archive is null) {
            return;
        }

        string? parent = Path.GetDirectoryName(fullPath);
        while (parent is { Length: > 0 } && !parent.Equals(archive.Archive, StringComparison.OrdinalIgnoreCase)) {
            _folders.Add(parent);
            parent = Path.GetDirectoryName(parent);
        }
        _folders.Add(archive.Archive);
    }

    private static bool IsChildOf(string path, string folder) {
        return string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);
    }

    private static FileSystemEntry Entry(string path, EntryKind kind, long? size) {
        return new FileSystemEntry(
            Name: Path.GetFileName(path),
            FullPath: path,
            Kind: kind,
            Size: size,
            ModifiedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }
}
