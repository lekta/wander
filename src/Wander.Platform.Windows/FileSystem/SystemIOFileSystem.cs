using System.ComponentModel;
using System.Runtime.InteropServices;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.FileSystem;

public sealed class SystemIOFileSystem : IFileSystem {
    private static readonly IComparer<string> _naturalNameComparer =
        Comparer<string>.Create((a, b) => StrCmpLogicalW(a, b));


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

        // EnumerateFileSystemInfos, not EnumerateDirectories + a fresh
        // DirectoryInfo/FileInfo per hit: the enumerator already carries
        // attributes, size and timestamps from the directory scan, while
        // constructing an info object by path makes every property access
        // pay for its own stat call. On a folder of tens of thousands of
        // files that difference is the second or two the list spent blank.
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos()) {
            if (info is DirectoryInfo dir) {
                folderLikes.Add(BuildEntry(dir, EntryKind.Directory));
                continue;
            }

            var entry = BuildEntry(info, EntryKind.File);
            if (entry.LinksToDirectory) {
                folderLikes.Add(entry);
            } else {
                files.Add(entry);
            }
        }

        // Explorer-style natural sort for the Name tiebreaker (numbers,
        // special chars, "_" before letters). StrCmpLogicalW is what
        // Explorer itself uses; we hand it to EntryComparers via the
        // optional name-comparer hook. The folders-first split (and the
        // single merged stream when it is off, so "newest first" really
        // does put the newest item on top regardless of kind) lives in
        // EntryComparers.Sort — the pass that re-sorts a listing once its
        // ratings have been read goes through the same code.
        var all = new List<FileSystemEntry>(folderLikes.Count + files.Count);
        all.AddRange(folderLikes);
        all.AddRange(files);

        return EntryComparers.Sort(all, options, _naturalNameComparer);
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

    /// <summary>
    /// <c>File.Copy</c> when nobody is watching, <c>CopyFileEx</c> when
    /// somebody is. The two have the same semantics - attributes, alternate
    /// data streams, no partial file left behind on failure - but only the
    /// second says how far it has got, and takes a "stop now" flag the
    /// system checks between chunks. Cancelling mid-file is the reason it is
    /// here at all: a 5 GB copy that can only be stopped between files
    /// cannot be stopped.
    /// </summary>
    public void CopyFile(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default) {

        if (bytesCopied is null && !ct.CanBeCanceled) {
            File.Copy(source, destination, overwrite);

            return;
        }

        ct.ThrowIfCancellationRequested();
        CopyFileWithProgress(source, destination, overwrite, bytesCopied, ct);
    }

    public void CopyDirectory(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default) {

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source)) {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetFileName(file));
            CopyFile(file, target, overwrite, bytesCopied, ct);
        }

        foreach (var dir in Directory.EnumerateDirectories(source)) {
            // Junctions / directory symlinks are not descended into: a link
            // pointing at an ancestor would make this recursion infinite and
            // flood the destination with nested copies until the disk fills.
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) {
                continue;
            }

            var target = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, target, overwrite, bytesCopied, ct);
        }
    }

    public void MoveEntry(string source, string destination,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default) {

        if (Directory.Exists(source)) {
            try {
                Directory.Move(source, destination);
            } catch (IOException) when (RootsDiffer(source, destination)) {
                // Directory.Move can't span volumes; fall back to recursive
                // copy + delete. File.Move handles cross-volume by itself.
                CopyDirectory(source, destination, overwrite: false, bytesCopied, ct);
                Directory.Delete(source, recursive: true);
            }

            return;
        }

        // Within one volume a move is a rename: nothing to watch, nothing to
        // stop. Across volumes it is a copy, and then it is worth both.
        if ((bytesCopied is null && !ct.CanBeCanceled) || !RootsDiffer(source, destination)) {
            File.Move(source, destination);

            return;
        }

        CopyFileWithProgress(source, destination, overwrite: false, bytesCopied, ct);
        File.Delete(source);
    }

    public void Rename(string path, string newName) {
        string parent = Directory.GetParent(path)?.FullName
            ?? throw new InvalidOperationException("Cannot rename a root entry.");
        string target = Path.Combine(parent, newName);
        MoveEntry(path, target);
    }

    public byte[] ReadAllBytes(string path) {
        return File.ReadAllBytes(path);
    }

    public Stream OpenRead(string path) {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.SequentialScan);
    }

    public void ReplaceAtomic(string path, byte[] content) {
        // Temp file in the same directory so the swap stays on one volume.
        string temp = path + TransientFiles.ReplaceSuffix;
        try {
            File.WriteAllBytes(temp, content);
            if (File.Exists(path)) {
                // File.Replace is the atomic swap; it also carries the
                // original's attributes over to the replacement. No backup
                // file — the undo stack is where the old value lives.
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            } else {
                File.Move(temp, path);
            }
        } catch {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Returns true if the given file is a <c>.lnk</c> shortcut that
    /// resolves to an existing directory. Used at enumeration time so we
    /// can sort folder-shortcuts with folders.
    /// </summary>
    private static bool IsFolderShortcut(string path) {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
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


    /// <summary>
    /// One file through <c>CopyFileEx</c>, reporting deltas and honouring
    /// the token. A cancelled copy throws
    /// <see cref="OperationCanceledException"/>; the system removes the
    /// partial destination itself (COPY_FILE_RESTARTABLE is not set), so
    /// there is no tail to clean up here.
    /// </summary>
    private static void CopyFileWithProgress(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied, CancellationToken ct) {

        long reported = 0;
        int cancel = 0;

        // Kept in a local, and alive across the call: the unmanaged side
        // holds a pointer to it for the whole copy.
        CopyProgressRoutine routine = (_, transferred, _, _, _, _, _, _, _) => {
            if (ct.IsCancellationRequested) {
                return PROGRESS_CANCEL;
            }
            if (bytesCopied is not null && transferred > reported) {
                bytesCopied.Report(transferred - reported);
                reported = transferred;
            }

            return PROGRESS_CONTINUE;
        };

        bool ok = CopyFileExW(
            source, destination, routine, IntPtr.Zero, ref cancel,
            overwrite ? 0 : COPY_FILE_FAIL_IF_EXISTS);
        int error = Marshal.GetLastWin32Error();
        GC.KeepAlive(routine);
        if (ok) {
            return;
        }

        if (error == ERROR_REQUEST_ABORTED) {
            ct.ThrowIfCancellationRequested();

            throw new OperationCanceledException(ct);
        }

        // The system's own words first ("The process cannot access the
        // file", "Access is denied"): they reach the status bar as they are.
        throw new IOException(
            $"{new Win32Exception(error).Message} ({source} -> {destination})",
            unchecked((int)0x80070000 | error));
    }


    private static bool RootsDiffer(string a, string b) {
        return !string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // Best effort: the write already failed, and a stray temp file
            // is not worth masking the original exception with.
        }
    }


    private static FileSystemEntry BuildEntry(string path, EntryKind kind) {
        return kind == EntryKind.Directory
            ? BuildEntry(new DirectoryInfo(path), EntryKind.Directory)
            : BuildEntry(new FileInfo(path), EntryKind.File);
    }

    /// <summary>
    /// Builds an entry from an already-populated <see cref="FileSystemInfo"/>.
    /// Enumeration hands in objects the directory scan filled for free;
    /// the by-path overload above constructs one and pays a stat call.
    /// </summary>
    private static FileSystemEntry BuildEntry(FileSystemInfo info, EntryKind kind) {
        var attributes = SafeAttributes(info);

        return new FileSystemEntry(
            Name: info.Name,
            FullPath: info.FullName,
            Kind: kind,
            Size: info is FileInfo file ? SafeLong(() => file.Length) : null,
            ModifiedUtc: SafeUtc(() => info.LastWriteTimeUtc),
            IsHidden: attributes.HasFlag(FileAttributes.Hidden),
            IsReadOnly: attributes.HasFlag(FileAttributes.ReadOnly),
            IsSystem: attributes.HasFlag(FileAttributes.System),
            LinksToDirectory: kind == EntryKind.File && IsFolderShortcut(info.FullName));
    }

    private static FileAttributes SafeAttributes(FileSystemInfo info) {
        try {
            return info.Attributes;
        } catch {
            // Deleted between the scan and this read, or a path the process
            // may enumerate but not stat. No attributes = no special styling.
            return FileAttributes.None;
        }
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


    // --- P/Invoke ------------------------------------------------------

    private const int PROGRESS_CONTINUE = 0;
    private const int PROGRESS_CANCEL = 1;
    private const int COPY_FILE_FAIL_IF_EXISTS = 0x00000001;
    private const int ERROR_REQUEST_ABORTED = 1235;

    private delegate int CopyProgressRoutine(
        long totalFileSize, long totalBytesTransferred,
        long streamSize, long streamBytesTransferred,
        uint streamNumber, uint callbackReason,
        IntPtr sourceFile, IntPtr destinationFile, IntPtr data);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CopyFileExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CopyFileExW(
        string existingFileName, string newFileName,
        CopyProgressRoutine? progressRoutine, IntPtr data,
        ref int cancel, int copyFlags);
}
