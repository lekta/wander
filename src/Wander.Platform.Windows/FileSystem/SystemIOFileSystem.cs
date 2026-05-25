using Wander.Core.FileSystem;

namespace Wander.Platform.Windows.FileSystem;

public sealed class SystemIOFileSystem : IFileSystem {
    public bool DirectoryExists(string path) {
        return Directory.Exists(path);
    }

    public bool FileExists(string path) {
        return File.Exists(path);
    }


    public IReadOnlyList<FileSystemEntry> Enumerate(string path) {
        var result = new List<FileSystemEntry>();

        foreach (var dir in Directory.EnumerateDirectories(path)) {
            result.Add(BuildEntry(dir, EntryKind.Directory));
        }

        foreach (var file in Directory.EnumerateFiles(path)) {
            result.Add(BuildEntry(file, EntryKind.File));
        }

        return result;
    }

    public IReadOnlyList<FileSystemEntry> GetRoots() {
        var result = new List<FileSystemEntry>();

        foreach (var drive in DriveInfo.GetDrives()) {
            if (!drive.IsReady) {
                continue;
            }

            string name = string.IsNullOrEmpty(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

            result.Add(new FileSystemEntry(
                Name: name,
                FullPath: drive.RootDirectory.FullName,
                Kind: EntryKind.Drive,
                Size: null,
                ModifiedUtc: DateTime.MinValue,
                IsHidden: false,
                IsReadOnly: false));
        }

        return result;
    }

    public string? GetParent(string path) {
        var parent = Directory.GetParent(path);
        return parent?.FullName;
    }


    public void CreateDirectory(string path) {
        Directory.CreateDirectory(path);
    }

    public void DeleteFile(string path) {
        File.Delete(path);
    }

    public void DeleteDirectory(string path, bool recursive) {
        Directory.Delete(path, recursive);
    }

    public void CopyFile(string source, string destination, bool overwrite) {
        File.Copy(source, destination, overwrite);
    }

    public void CopyDirectory(string source, string destination, bool overwrite) {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source)) {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite);
        }

        foreach (var dir in Directory.EnumerateDirectories(source)) {
            var target = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, target, overwrite);
        }
    }

    public void MoveEntry(string source, string destination) {
        if (Directory.Exists(source)) {
            Directory.Move(source, destination);
            return;
        }

        File.Move(source, destination);
    }

    public void Rename(string path, string newName) {
        string parent = Directory.GetParent(path)?.FullName
            ?? throw new InvalidOperationException("Cannot rename a root entry.");
        string target = Path.Combine(parent, newName);
        MoveEntry(path, target);
    }


    private static FileSystemEntry BuildEntry(string path, EntryKind kind) {
        if (kind == EntryKind.Directory) {
            var info = new DirectoryInfo(path);
            return new FileSystemEntry(
                Name: info.Name,
                FullPath: info.FullName,
                Kind: EntryKind.Directory,
                Size: null,
                ModifiedUtc: SafeUtc(() => info.LastWriteTimeUtc),
                IsHidden: info.Attributes.HasFlag(FileAttributes.Hidden),
                IsReadOnly: info.Attributes.HasFlag(FileAttributes.ReadOnly));
        }

        var fileInfo = new FileInfo(path);
        return new FileSystemEntry(
            Name: fileInfo.Name,
            FullPath: fileInfo.FullName,
            Kind: EntryKind.File,
            Size: SafeLong(() => fileInfo.Length),
            ModifiedUtc: SafeUtc(() => fileInfo.LastWriteTimeUtc),
            IsHidden: fileInfo.Attributes.HasFlag(FileAttributes.Hidden),
            IsReadOnly: fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly));
    }

    private static DateTime SafeUtc(Func<DateTime> f) {
        try {
            return f();
        } catch {
            return DateTime.MinValue;
        }
    }

    private static long? SafeLong(Func<long> f) {
        try {
            return f();
        } catch {
            return null;
        }
    }
}
