using System.Runtime.InteropServices;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.FileSystem;

public sealed class SystemIOFileSystem : IFileSystem {
    public bool DirectoryExists(string path) {
        return Directory.Exists(path);
    }

    public bool FileExists(string path) {
        return File.Exists(path);
    }


    public IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null) {
        var options = sort ?? SortOptions.Default;

        // Folder-likes go in one bucket, plain files in another. A
        // ".lnk → directory" is sorted with folders even though it's a
        // file on disk; OpenEntry already treats it as a folder when the
        // user double-clicks (TryFollowFolderShortcut), so sort grouping
        // matches behavioural grouping.
        var folderLikes = new List<FileSystemEntry>();
        var files = new List<FileSystemEntry>();

        foreach (var dir in Directory.EnumerateDirectories(path)) {
            folderLikes.Add(BuildEntry(dir, EntryKind.Directory));
        }
        foreach (var file in Directory.EnumerateFiles(path)) {
            var entry = BuildEntry(file, EntryKind.File);
            if (entry.LinksToDirectory) {
                folderLikes.Add(entry);
            } else {
                files.Add(entry);
            }
        }

        // Explorer-style natural sort for the Name tiebreaker (numbers,
        // special chars, "_" before letters). StrCmpLogicalW is what
        // Explorer itself uses; we hand it to EntryComparers via the
        // optional name-comparer hook.
        var comparer = EntryComparers.Build(options, _naturalNameComparer);

        if (options.GroupFoldersFirst) {
            folderLikes.Sort(comparer);
            files.Sort(comparer);
            var result = new List<FileSystemEntry>(folderLikes.Count + files.Count);
            result.AddRange(folderLikes);
            result.AddRange(files);
            return result;
        }

        // Single merged stream — folders mingle with files according to
        // the chosen key (so "newest first" actually puts the newest item
        // at the top regardless of kind).
        var merged = new List<FileSystemEntry>(folderLikes.Count + files.Count);
        merged.AddRange(folderLikes);
        merged.AddRange(files);
        merged.Sort(comparer);
        return merged;
    }

    private static readonly IComparer<string> _naturalNameComparer =
        Comparer<string>.Create((a, b) => StrCmpLogicalW(a, b));


    /// <summary>
    /// Returns true if the given file is a <c>.lnk</c> shortcut that
    /// resolves to an existing directory. Used at enumeration time so we
    /// can sort folder-shortcuts with folders.
    /// </summary>
    private static bool IsFolderShortcut(string path) {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (!ServiceLocator.IsRegistered<IShortcutService>()) {
            return false;
        }
        try {
            string? target = ServiceLocator.Get<IShortcutService>().Resolve(path);
            return !string.IsNullOrEmpty(target) && Directory.Exists(target);
        } catch {
            // Broken / dangling shortcut — treat as a regular file.
            return false;
        }
    }


    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

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
                IsReadOnly: false,
                IsSystem: false,
                LinksToDirectory: false));
        }

        return result;
    }

    public bool HasSubdirectories(string path) {
        try {
            return Directory.EnumerateDirectories(path).Any();
        } catch {
            return false;
        }
    }

    public string? GetParent(string path) {
        var parent = Directory.GetParent(path);
        return parent?.FullName;
    }

    public FileSystemEntry? GetEntry(string path) {
        if (Directory.Exists(path)) {
            return BuildEntry(path, EntryKind.Directory);
        }
        if (File.Exists(path)) {
            return BuildEntry(path, EntryKind.File);
        }
        return null;
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

    public void ClearReadOnly(string path) {
        if (File.Exists(path)) {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0) {
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }
            return;
        }
        if (Directory.Exists(path)) {
            var info = new DirectoryInfo(path);
            if ((info.Attributes & FileAttributes.ReadOnly) != 0) {
                info.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
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
            // Junctions / directory symlinks are not descended into: a link
            // pointing at an ancestor would make this recursion infinite and
            // flood the destination with nested copies until the disk fills.
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) {
                continue;
            }

            var target = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, target, overwrite);
        }
    }

    public void MoveEntry(string source, string destination) {
        if (Directory.Exists(source)) {
            try {
                Directory.Move(source, destination);
            } catch (IOException) when (RootsDiffer(source, destination)) {
                // Directory.Move can't span volumes; fall back to recursive
                // copy + delete. File.Move handles cross-volume by itself.
                CopyDirectory(source, destination, overwrite: false);
                Directory.Delete(source, recursive: true);
            }
            return;
        }

        File.Move(source, destination);
    }

    private static bool RootsDiffer(string a, string b) {
        return !string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
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
                IsReadOnly: info.Attributes.HasFlag(FileAttributes.ReadOnly),
                IsSystem: info.Attributes.HasFlag(FileAttributes.System),
                LinksToDirectory: false);
        }

        var fileInfo = new FileInfo(path);
        return new FileSystemEntry(
            Name: fileInfo.Name,
            FullPath: fileInfo.FullName,
            Kind: EntryKind.File,
            Size: SafeLong(() => fileInfo.Length),
            ModifiedUtc: SafeUtc(() => fileInfo.LastWriteTimeUtc),
            IsHidden: fileInfo.Attributes.HasFlag(FileAttributes.Hidden),
            IsReadOnly: fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly),
            IsSystem: fileInfo.Attributes.HasFlag(FileAttributes.System),
            LinksToDirectory: IsFolderShortcut(fileInfo.FullName));
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
