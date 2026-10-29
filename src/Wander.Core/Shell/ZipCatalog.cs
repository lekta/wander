using System.IO.Compression;
using System.Text;
using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Shell;

/// <summary>
/// A zip's central directory, read once with the BCL's <see cref="ZipArchive"/>:
/// every entry's name and unpacked size in one pass, where the shell gives
/// one level per call and reads the archive again for each. The census of
/// any folder inside - the same numbers the preview pane shows for a folder
/// on disk - is then counted from memory (PLAN, "Свой листинг zip").
///
/// <para>
/// Names: an entry flagged UTF-8 is read as UTF-8; one that is not is in
/// the code page of the DOS that wrote it, which is how zipfldr reads it
/// too - the machine's OEM page. Only 866 has a table here
/// (<see cref="EncodingProbe"/>); under any other page such names stay
/// Latin-1, a folder named with them is not found, and the caller falls
/// back to the shell.
/// </para>
///
/// <para>
/// A zip the shell lists as empty is listed from here too, a level at a
/// time (<see cref="List"/>, <see cref="ZipFallback"/>).
/// </para>
/// </summary>
public sealed class ZipCatalog {
    private readonly List<(string Path, long Size, DateTime ModifiedUtc)> _files;
    private readonly HashSet<string> _folders;

    /// <summary>The files by path, built on the first <see cref="SizeOf"/>: the census alone never needs it.</summary>
    private Dictionary<string, long>? _sizes;


    private ZipCatalog(List<(string Path, long Size, DateTime ModifiedUtc)> files, HashSet<string> folders) {
        _files = files;
        _folders = folders;
    }


    /// <summary>Files in the whole archive.</summary>
    public int FileCount => _files.Count;


    /// <summary>Reads the directory of the zip in <paramref name="stream"/>.</summary>
    /// <param name="oemCodePage">The machine's OEM code page, for names not flagged UTF-8.</param>
    /// <exception cref="InvalidDataException">Not a zip, or a broken one.</exception>
    public static ZipCatalog Read(Stream stream, int oemCodePage, CancellationToken ct = default) {
        using var zip = Open(stream, oemCodePage);

        var files = new List<(string Path, long Size, DateTime ModifiedUtc)>(zip.Entries.Count);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries) {
            ct.ThrowIfCancellationRequested();
            string path = PathOf(entry);
            if (path.Length == 0) {
                continue;
            }

            bool isFolder = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            if (isFolder) {
                folders.Add(path);
            } else {
                files.Add((path, entry.Length, entry.LastWriteTime.UtcDateTime));
            }
            // A zip need not list its folders: a file's path is enough to
            // make every folder above it.
            for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1)) {
                if (!folders.Add(path[..slash])) {
                    break;
                }
            }
        }

        return new ZipCatalog(files, folders);
    }

    /// <summary>
    /// Everything under <paramref name="inner"/> (<see cref="ArchivePath.Inner"/>,
    /// "" for the whole archive): files, folders, their unpacked size and
    /// the types that fill it. Null when there is no such folder in it.
    /// </summary>
    public FolderStats? Census(string inner, int maxTypes = 8) {
        string folder = inner.Replace('\\', '/').Trim('/');
        if (folder.Length > 0 && !_folders.Contains(folder)) {
            return null;
        }

        string prefix = folder.Length == 0 ? "" : folder + "/";
        int files = 0;
        long total = 0;
        var byExtension = new Dictionary<string, (int Count, long Size)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, size, _) in _files) {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            files++;
            total += size;
            string ext = FolderStatistics.ExtensionOf(path[(path.LastIndexOf('/') + 1)..]);
            var bucket = byExtension.TryGetValue(ext, out var found) ? found : default;
            byExtension[ext] = (bucket.Count + 1, bucket.Size + size);
        }
        int folders = _folders.Count(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.Length > prefix.Length);

        return new FolderStats(files, folders, total, FolderStatistics.TopTypes(byExtension, maxTypes), false);
    }

    /// <summary>The unpacked size of the file at <paramref name="inner"/>; null when no file of the archive is there.</summary>
    public long? SizeOf(string inner) {
        if (_sizes is null) {
            var sizes = new Dictionary<string, long>(_files.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (path, size, _) in _files) {
                // A zip may hold the same name twice; the shell takes the first.
                sizes.TryAdd(path, size);
            }
            _sizes = sizes;
        }

        return _sizes.TryGetValue(Normalize(inner), out long found) ? found : null;
    }

    /// <summary>True for the archive's root and for every folder in it, listed or implied by a file's path.</summary>
    public bool IsFolder(string inner) {
        string folder = Normalize(inner);

        return folder.Length == 0 || _folders.Contains(folder);
    }

    /// <summary>
    /// One level of the archive as the shell would list it: folders with
    /// no size and no date, files with both, the full path the archive's
    /// own followed by the entry's. A name Windows cannot hold - "..", a
    /// colon - is left out: it could not be extracted anyway, and ".."
    /// would lead out of the target folder. Null when there is no such
    /// folder in the archive.
    /// </summary>
    public IReadOnlyList<FileSystemEntry>? List(string archive, string inner) {
        string folder = Normalize(inner);
        if (!IsFolder(folder)) {
            return null;
        }

        string prefix = folder.Length == 0 ? "" : folder + "/";
        var rows = new List<FileSystemEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in _folders) {
            if (ChildName(path, prefix) is { } name && seen.Add(name)) {
                rows.Add(Row(archive, path, name, EntryKind.Directory, null, DateTime.MinValue));
            }
        }
        foreach (var (path, size, modified) in _files) {
            if (ChildName(path, prefix) is { } name && seen.Add(name)) {
                rows.Add(Row(archive, path, name, EntryKind.File, size, modified));
            }
        }

        return rows;
    }

    /// <summary>The zip in <paramref name="stream"/>, its names read the way <see cref="Read"/> reads them.</summary>
    internal static ZipArchive Open(Stream stream, int oemCodePage) {
        var names = oemCodePage == 866 ? new Dos866Names() : (Encoding)Encoding.Latin1;

        return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: names);
    }

    /// <summary>The entry's path with forward slashes and none at either end - the catalog's key.</summary>
    internal static string PathOf(ZipArchiveEntry entry) => Normalize(entry.FullName);

    /// <summary>A name a file or folder on disk can have: not empty, not "." or "..", no character Windows refuses.</summary>
    internal static bool IsPlainName(string name) {
        return name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }


    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    /// <summary>The name of <paramref name="path"/> when it lies directly under <paramref name="prefix"/> and is plain; null otherwise.</summary>
    private static string? ChildName(string path, string prefix) {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || path.IndexOf('/', prefix.Length) >= 0) {
            return null;
        }
        string name = path[prefix.Length..];

        return IsPlainName(name) ? name : null;
    }

    private static FileSystemEntry Row(string archive, string path, string name, EntryKind kind, long? size, DateTime modifiedUtc) {
        return new FileSystemEntry(
            Name: name,
            FullPath: Path.Combine(archive, path.Replace('/', '\\')),
            Kind: kind,
            Size: size,
            ModifiedUtc: modifiedUtc,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }


    /// <summary>
    /// Code page 866 as the <see cref="Encoding"/> <see cref="ZipArchive"/>
    /// asks for, decoding only: the archive is opened to be read, and the
    /// table is <see cref="EncodingProbe"/>'s - no CodePages package in Core.
    /// </summary>
    private sealed class Dos866Names : Encoding {
        public override int GetByteCount(char[] chars, int index, int count) => count;

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) {
            throw new NotSupportedException("Reading only.");
        }

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) {
            string text = EncodingProbe.Decode(bytes.AsSpan(byteIndex, byteCount), TextEncodingKind.Dos866);
            text.CopyTo(0, chars, charIndex, text.Length);

            return text.Length;
        }

        public override int GetMaxByteCount(int charCount) => charCount;

        public override int GetMaxCharCount(int byteCount) => byteCount;
    }
}
