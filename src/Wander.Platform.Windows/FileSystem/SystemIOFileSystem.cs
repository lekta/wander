using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.FileSystem;

public sealed class SystemIOFileSystem : IFileSystem {
    /// <summary>What a copy keeps of the source's attributes; the rest is the volume's business.</summary>
    private const FileAttributes CopiedAttributes =
        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed;

    /// <summary>FILE_FLAG_NO_BUFFERING: reads that go to the medium, not to the cache.</summary>
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    private static readonly IComparer<string> _naturalNameComparer =
        Comparer<string>.Create((a, b) => StrCmpLogicalW(a, b));

    [ThreadStatic]
    private static Memory<byte>? _aligned;


    public bool DirectoryExists(string path) {
        return Directory.Exists(path);
    }

    public bool FileExists(string path) {
        return File.Exists(path);
    }


    public IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null, CancellationToken ct = default) {
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
            ct.ThrowIfCancellationRequested();
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

    public IReadOnlyList<string> FileNamesLike(string directory, string pattern) {
        try {
            return Directory.EnumerateFiles(directory, pattern).Select(Path.GetFileName).ToArray()!;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return Array.Empty<string>();
        }
    }

    public DateTime? GetCreationTimeUtc(string path) {
        try {
            if (Directory.Exists(path)) {
                return Directory.GetCreationTimeUtc(path);
            }
            if (File.Exists(path)) {
                return File.GetCreationTimeUtc(path);
            }
        } catch (IOException) {
            // A share that went away between the two calls, a volume that
            // does not keep the time: "unknown" is the honest answer.
        } catch (UnauthorizedAccessException) {
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
    /// <c>File.Copy</c> when nobody is watching, Wander's own loop when
    /// somebody is (<see cref="CopyBytes"/>): it says how far it has got,
    /// stops in the middle of a file, hashes what it reads and can read the
    /// copy back. Cancelling mid-file is why there is a loop at all: a 5 GB
    /// copy that can only be stopped between files cannot be stopped.
    ///
    /// <para>
    /// The watched copy goes under a temporary name and takes the real one
    /// only when it is whole (<see cref="CopyThroughTemp"/>).
    /// </para>
    /// </summary>
    public CopyReport CopyFile(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default, CopyOptions? options = null) {

        options ??= CopyOptions.Plain;
        if (bytesCopied is null && !ct.CanBeCanceled && !options.Verify) {
            File.Copy(source, destination, overwrite);

            return new CopyReport(CopyEngine.System, false);
        }

        ct.ThrowIfCancellationRequested();

        return CopyThroughTemp(source, destination, overwrite, bytesCopied, ct, options);
    }

    public CopyReport CopyDirectory(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default, CopyOptions? options = null) {

        Directory.CreateDirectory(destination);
        var report = CopyReport.None;

        foreach (var file in Directory.EnumerateFiles(source)) {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetFileName(file));
            report = report.And(CopyFile(file, target, overwrite, bytesCopied, ct, options));
        }

        foreach (var dir in Directory.EnumerateDirectories(source)) {
            // Junctions / directory symlinks are not descended into: a link
            // pointing at an ancestor would make this recursion infinite and
            // flood the destination with nested copies until the disk fills.
            // Not silently: the operation's record names each one.
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) {
                report = report.And(new CopyReport(CopyEngine.None, false, new[] { dir }));
                continue;
            }

            var target = Path.Combine(destination, Path.GetFileName(dir));
            report = report.And(CopyDirectory(dir, target, overwrite, bytesCopied, ct, options));
        }

        return report;
    }

    /// <summary>
    /// A file off a phone or a camera (PLAN Y4c), the way <see cref="OwnCopy"/>
    /// makes a copy: the size claimed first, the bytes hashed on the way,
    /// flushed to the medium, read back past the cache when asked, dated as
    /// on the device - under the temporary name, which gives way to the real
    /// one only when the file is whole and checked.
    /// </summary>
    public CopyReport WriteFromStream(Stream source, long length, string destination, DateTime? modifiedUtc,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default, CopyOptions? options = null) {

        options ??= CopyOptions.Plain;
        ct.ThrowIfCancellationRequested();
        if (File.Exists(destination) || Directory.Exists(destination)) {
            throw new IOException(
                $"{new Win32Exception(ERROR_FILE_EXISTS).Message} ({destination})",
                unchecked((int)0x80070000 | ERROR_FILE_EXISTS));
        }

        string temp = TransientFiles.TempNameFor(destination);
        TryDelete(temp);
        try {
            byte[] hash;
            using (var to = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0, FileOptions.SequentialScan)) {
                if (length > 0) {
                    to.SetLength(length);
                }
                hash = StreamCopy.Copy(source, to, bytesCopied, ct);
                // A device that gave fewer bytes than it said leaves no
                // claimed tail of zeros behind.
                if (to.Position != to.Length) {
                    to.SetLength(to.Position);
                }
                to.Flush(flushToDisk: true);
            }

            if (options.Verify && !HashOf(temp, unbuffered: true, ct).AsSpan().SequenceEqual(hash)) {
                throw new CopyMismatchException(destination, temp);
            }
            if (modifiedUtc is { } modified) {
                File.SetLastWriteTimeUtc(temp, modified);
            }
            File.Move(temp, destination, overwrite: false);

            return new CopyReport(CopyEngine.Own, options.Verify);
        } catch {
            TryDelete(temp);

            throw;
        }
    }

    public CopyReport MoveEntry(string source, string destination,
        IProgress<long>? bytesCopied = null, CancellationToken ct = default, CopyOptions? options = null) {

        if (Directory.Exists(source)) {
            try {
                Directory.Move(source, destination);

                return CopyReport.None;
            } catch (IOException) when (RootsDiffer(source, destination)) {
                // Directory.Move can't span volumes; fall back to recursive
                // copy + delete. File.Move handles cross-volume by itself.
                // A copy that failed or was cancelled half-way is ours to
                // take back - the source is still whole where it was - unless
                // the folder was there before we started.
                bool ours = !Directory.Exists(destination);
                CopyReport report;
                try {
                    report = CopyDirectory(source, destination, overwrite: false, bytesCopied, ct, options);
                } catch {
                    if (ours) {
                        TryDeleteDirectory(destination);
                    }

                    throw;
                }
                // A link the copy left out stays where it is, and so do the
                // folders holding it: a recursive delete took it with the
                // source, and it was in neither place.
                if (report.SkippedLinks is { Count: > 0 }) {
                    DeleteAllButLinks(source);
                } else {
                    Directory.Delete(source, recursive: true);
                }

                return report;
            }
        }

        // Within one volume a move is a rename: nothing to watch, nothing to
        // stop. Across volumes it is a copy, and then it is worth both - and
        // the source goes only once the copy is whole, and read back when
        // asked for.
        options ??= CopyOptions.Plain;
        if ((bytesCopied is null && !ct.CanBeCanceled && !options.Verify) || !RootsDiffer(source, destination)) {
            File.Move(source, destination);

            return CopyReport.None;
        }

        var moved = CopyThroughTemp(source, destination, overwrite: false, bytesCopied, ct, options);
        try {
            File.Delete(source);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // The source would not go - a card gone read-only, a held file:
            // the copy goes instead, so the move is not done rather than
            // half-done, and a retry finds the target free and says why the
            // source stays (review of sprint 2).
            TryDelete(destination);

            throw;
        }

        return moved;
    }

    public void Rename(string path, string newName) {
        string parent = Directory.GetParent(path)?.FullName
            ?? throw new InvalidOperationException("Cannot rename a root entry.");
        string target = Path.Combine(parent, newName);
        MoveEntry(path, target);
    }

    public byte[] ReadAllBytes(string path) {
        return SharedRead.ReadAllBytes(path);
    }

    public byte[] ReadAllBytesForUpdate(string path) {
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

    public void WriteNew(string path, byte[] content) {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(content);
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
    /// A copy under <see cref="TransientFiles.TempNameFor"/> beside the
    /// target, renamed to the target once whole. The folder being copied
    /// into never shows a half-written file - the watcher keeps temporary
    /// names to itself - and a failed or cancelled copy leaves nothing behind
    /// at the real name. A temporary file left by a copy that died with the
    /// process is ours, and goes first.
    /// </summary>
    private static CopyReport CopyThroughTemp(string source, string destination, bool overwrite,
        IProgress<long>? bytesCopied, CancellationToken ct, CopyOptions options) {

        // What COPY_FILE_FAIL_IF_EXISTS said before the bytes moved: the
        // rename at the end would say it too, but only after the whole copy.
        if (!overwrite && (File.Exists(destination) || Directory.Exists(destination))) {
            throw new IOException(
                $"{new Win32Exception(ERROR_FILE_EXISTS).Message} ({source} -> {destination})",
                unchecked((int)0x80070000 | ERROR_FILE_EXISTS));
        }

        string temp = TransientFiles.TempNameFor(destination);
        TryDelete(temp);
        try {
            var report = CopyBytes(source, temp, bytesCopied, ct, options);
            File.Move(temp, destination, overwrite);

            return report;
        } catch {
            TryDelete(temp);

            throw;
        }
    }

    /// <summary>
    /// The bytes of one file into <paramref name="temp"/>. Wander's own loop
    /// (<see cref="OwnCopy"/>) unless the file needs what only Windows'
    /// engine keeps (<see cref="SystemCopyReason"/>) or the debug switch says
    /// so; a loop that fails in a way nobody expects hands the file to
    /// Windows' engine, within the same try. Windows' copies are checked by
    /// reading both sides back - there was no hash on the way.
    /// </summary>
    private static CopyReport CopyBytes(string source, string temp, IProgress<long>? progress, CancellationToken ct, CopyOptions options) {
        string? system = options.SystemOnly ? "debug switch" : SystemCopyReason(source, temp);
        if (system is null) {
            long reported = 0;
            var counted = progress is null ? null : new CountedProgress(progress, delta => reported += delta);
            try {
                OwnCopy(source, temp, counted, ct, options.Verify);

                return new CopyReport(CopyEngine.Own, options.Verify);
            } catch (Exception ex) when (IsUnfamiliar(ex)) {
                Log.Warn($"Own copy failed: {source} - {ex.GetType().Name}: {ex.Message} (0x{ex.HResult:X8}); copying with Windows' engine");
                TryDelete(temp);
                if (reported != 0) {
                    progress!.Report(-reported);
                }
            }
        } else {
            Log.Detail($"Copy with Windows' engine: {Log.Path(source)} ({system})");
        }

        CopyFileWithProgress(source, temp, overwrite: false, progress, ct);
        if (options.Verify && !HashOf(source, unbuffered: false, ct).AsSpan().SequenceEqual(HashOf(temp, unbuffered: true, ct))) {
            throw new CopyMismatchException(source, temp);
        }

        return new CopyReport(CopyEngine.System, options.Verify);
    }

    /// <summary>
    /// Wander's loop (<see cref="StreamCopy"/>): the whole size claimed
    /// first - a FAT32 card says "too large" before a byte moves, and the
    /// file lies in one piece - then buffer by buffer, hashed on the way,
    /// flushed to the medium at the end. Read back when asked, past the
    /// system's cache, so it is the medium that answers. Then the attributes
    /// and the time of the last change; the creation time is new, as
    /// Explorer's is. The source is shared for writing, as CopyFileEx shares
    /// it: a document open in its program (a .docx, a mailbox, a log) is
    /// copied as it lies, not refused as held (review of sprint 2).
    /// </summary>
    private static void OwnCopy(string source, string temp, IProgress<long>? progress, CancellationToken ct, bool verify) {
        byte[] hash;
        using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.SequentialScan))
        using (var to = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0, FileOptions.SequentialScan)) {
            to.SetLength(from.Length);
            hash = StreamCopy.Copy(from, to, progress, ct);
            to.Flush(flushToDisk: true);
        }

        if (verify && !HashOf(temp, unbuffered: true, ct).AsSpan().SequenceEqual(hash)) {
            throw new CopyMismatchException(source, temp);
        }

        File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(source));
        var attributes = File.GetAttributes(source) & CopiedAttributes;
        if (attributes != 0) {
            File.SetAttributes(temp, attributes);
        }
    }

    /// <summary>
    /// Why a file goes through Windows' engine, or null when Wander's loop
    /// will do (FILEOPS, "fallback to CopyFileEx"): encrypted or sparse - the
    /// loop would decrypt or fill it in silently; extra streams - the
    /// "downloaded from the internet" mark among them; both ends on one
    /// server - the server copies without the bytes crossing the network;
    /// one ReFS volume - a block clone, instant.
    /// </summary>
    private static string? SystemCopyReason(string source, string destination) {
        var attributes = File.GetAttributes(source);
        if ((attributes & FileAttributes.Encrypted) != 0) {
            return "encrypted";
        }
        if ((attributes & FileAttributes.SparseFile) != 0) {
            return "sparse";
        }
        if (HasAlternateStreams(source)) {
            return "alternate data streams";
        }
        if (OnOneServer(source, destination)) {
            return "both ends on one server";
        }
        if (OnOneReFsVolume(source, destination)) {
            return "one ReFS volume";
        }

        return null;
    }

    /// <summary>A stream besides the file's own data - <c>:Zone.Identifier</c> and the like.</summary>
    private static bool HasAlternateStreams(string path) {
        IntPtr find = FindFirstStreamW(path, FindStreamInfoStandard, out var data, 0);
        if (find == INVALID_HANDLE_VALUE) {
            // No streams to list (FAT, exFAT) or nothing but the data.
            return false;
        }

        try {
            do {
                if (!string.Equals(data.cStreamName, "::$DATA", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            } while (FindNextStreamW(find, out data));

            return false;
        } finally {
            FindClose(find);
        }
    }

    /// <summary>Both ends on a share of one server - or both on network drives, whose server is not worth asking.</summary>
    private static bool OnOneServer(string a, string b) {
        string rootA = Path.GetPathRoot(a) ?? "";
        string rootB = Path.GetPathRoot(b) ?? "";
        if (rootA.StartsWith(@"\\", StringComparison.Ordinal) && rootB.StartsWith(@"\\", StringComparison.Ordinal)) {
            return string.Equals(ServerOf(rootA), ServerOf(rootB), StringComparison.OrdinalIgnoreCase);
        }

        return KindOf(rootA) == DriveType.Network && KindOf(rootB) == DriveType.Network;
    }

    private static bool OnOneReFsVolume(string a, string b) {
        string root = Path.GetPathRoot(a) ?? "";
        if (root.Length == 0 || RootsDiffer(a, b) || root.StartsWith(@"\\", StringComparison.Ordinal)) {
            return false;
        }

        try {
            return string.Equals(new DriveInfo(root).DriveFormat, "ReFS", StringComparison.OrdinalIgnoreCase);
        } catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) {
            return false;
        }
    }

    private static string ServerOf(string uncRoot) {
        string trimmed = uncRoot.TrimStart('\\');
        int slash = trimmed.IndexOf('\\');

        return slash < 0 ? trimmed : trimmed[..slash];
    }

    private static DriveType KindOf(string root) {
        try {
            return root.Length == 0 ? DriveType.Unknown : new DriveInfo(root).DriveType;
        } catch (ArgumentException) {
            return DriveType.Unknown;
        }
    }

    /// <summary>
    /// A failure of Wander's loop that is not the file's or the disk's - not
    /// a missing file, a refusal, a full disk, a held file, a transient read,
    /// a cancel - and so may well be the loop's: the file is handed to
    /// Windows' engine instead.
    /// </summary>
    private static bool IsUnfamiliar(Exception ex) {
        if (ex is OperationCanceledException or CopyMismatchException || TransientIo.IsTransient(ex)) {
            return false;
        }

        return TransientIo.Win32Code(ex) switch {
            2 or 3 or 5 or 19 or 32 or 33 or 39 or 80 or 112 or 183 or 206 or 223 => false,
            _ => ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException,
        };
    }

    /// <summary>
    /// The SHA-256 of a file, read to the end. <paramref name="unbuffered"/>
    /// reads past the system's cache - a copy just written would otherwise
    /// be read back from memory, and checked against itself; where the
    /// volume will not read that way, it is read the ordinary way.
    /// </summary>
    private static byte[] HashOf(string path, bool unbuffered, CancellationToken ct) {
        if (unbuffered) {
            try {
                return UnbufferedHash(path, ct);
            } catch (Exception ex) when (ex is ArgumentException or NotSupportedException || TransientIo.Win32Code(ex) == ERROR_INVALID_PARAMETER) {
                Log.Detail($"Unbuffered read refused for {Log.Path(path)} ({ex.Message}): reading it through the cache");
            }
        }

        // Shared for writing: a source open in its program is read as the copy read it.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.SequentialScan);

        return StreamCopy.Hash(stream, ct);
    }

    /// <summary>
    /// FILE_FLAG_NO_BUFFERING wants every read sector-aligned - its offset,
    /// its length and the memory it goes into. The buffer is pinned and
    /// aligned to 4 KB, a whole number of any sector size in use; reads are
    /// whole buffers, and the last one comes back short at the end of the
    /// file.
    /// </summary>
    private static byte[] UnbufferedHash(string path, CancellationToken ct) {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            NoBuffering | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = AlignedBuffer();
        long offset = 0;
        while (true) {
            ct.ThrowIfCancellationRequested();
            int read = RandomAccess.Read(handle, buffer.Span, offset);
            if (read == 0) {
                break;
            }

            hash.AppendData(buffer.Span[..read]);
            offset += read;
            if (read < buffer.Length) {
                break;
            }
        }

        return hash.GetHashAndReset();
    }

    /// <summary>One pinned, sector-aligned buffer per thread for <see cref="UnbufferedHash"/>.</summary>
    private static Memory<byte> AlignedBuffer() {
        if (_aligned is { } kept) {
            return kept;
        }

        const int align = 4096;
        byte[] raw = GC.AllocateArray<byte>(StreamCopy.BufferSize + align, pinned: true);
        long address = Marshal.UnsafeAddrOfPinnedArrayElement(raw, 0).ToInt64();
        int skip = (int)((align - (address % align)) % align);
        _aligned = raw.AsMemory(skip, StreamCopy.BufferSize);

        return _aligned.Value;
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
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        } catch {
            // Best effort: the write already failed, and a stray temp file
            // is not worth masking the original exception with.
        }
    }

    /// <summary>
    /// The source of a folder moved off its volume whose copy left links to
    /// folders out: what was copied goes, the links and the folders that
    /// hold them stay. Undoing the move pours the rest back in around them.
    /// </summary>
    /// <returns>True when the folder itself went - nothing in it was kept.</returns>
    private static bool DeleteAllButLinks(string folder) {
        foreach (string file in Directory.EnumerateFiles(folder)) {
            File.Delete(file);
        }

        bool kept = false;
        foreach (string dir in Directory.EnumerateDirectories(folder)) {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) {
                kept = true;
                continue;
            }
            kept |= !DeleteAllButLinks(dir);
        }
        if (kept) {
            return false;
        }

        Directory.Delete(folder);

        return true;
    }

    /// <summary>The part-copied folder of a failed move, best effort - like <see cref="TryDelete"/>.</summary>
    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // What could not go stays; the move's own error is the one to report.
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


    /// <summary>Passes reports on and counts them - what a failed loop has to take back.</summary>
    private sealed class CountedProgress : IProgress<long> {
        private readonly IProgress<long> _inner;
        private readonly Action<long> _count;


        public CountedProgress(IProgress<long> inner, Action<long> count) {
            _inner = inner;
            _count = count;
        }


        public void Report(long value) {
            _count(value);
            _inner.Report(value);
        }
    }


    // --- P/Invoke ------------------------------------------------------

    // Names as the Windows docs spell them, to the end of the file.
    // ReSharper disable InconsistentNaming

    private const int PROGRESS_CONTINUE = 0;
    private const int PROGRESS_CANCEL = 1;
    private const int COPY_FILE_FAIL_IF_EXISTS = 0x00000001;
    private const int ERROR_REQUEST_ABORTED = 1235;
    private const int ERROR_FILE_EXISTS = 80;
    private const int ERROR_INVALID_PARAMETER = 87;
    private const int FindStreamInfoStandard = 0;
    private const nint INVALID_HANDLE_VALUE = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_STREAM_DATA {
        public long StreamSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string cStreamName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(string fileName, int infoLevel, out WIN32_FIND_STREAM_DATA findStreamData, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(IntPtr findStream, out WIN32_FIND_STREAM_DATA findStreamData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr findFile);

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
