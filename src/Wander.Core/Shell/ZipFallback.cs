using System.IO.Compression;
using Wander.Core.FileSystem;
using Wander.Core.Logging;

namespace Wander.Core.Shell;

/// <summary>
/// Zips the shell lists as empty though they are not, read by Wander
/// instead. zipfldr gives no rows at all for a zip with an entry whose name
/// takes 260 bytes or more - in UTF-8 some 130 Cyrillic letters, its folders
/// included - and drops one of 257-259 bytes without a word (stand
/// 2026-10-29); cloud.mail.ru writes such zips.
///
/// <para>
/// The rule: a level of a zip the shell lists empty is looked up in the
/// zip's own directory (<see cref="ZipCatalog"/>), and when that has rows,
/// the whole archive is Wander's from then on - listing, sizes, bytes and
/// extraction. A level the shell lists with rows settles the other way. Both
/// are remembered for the session, by the archive's length and time: a zip
/// downloaded again under the same name is asked about again.
/// </para>
///
/// <para>
/// The bytes come out through the BCL's <see cref="ZipArchive"/>, which
/// neither checks a CRC nor refuses an encrypted entry - it hands out the
/// cipher text as if it were the file (stand 2026-10-29). Both checks are
/// here: a damaged entry is an <see cref="IOException"/> with
/// <c>ERROR_CRC</c> and leaves nothing on disk, an encrypted one an
/// <see cref="ArchiveLockedException"/>. Methods other than store and
/// deflate are an <see cref="InvalidDataException"/> from the BCL.
/// </para>
/// </summary>
public sealed class ZipFallback {
    /// <summary>ERROR_CRC as an HRESULT: what a damaged entry is reported as, the words being the app's.</summary>
    private const int CrcError = unchecked((int)0x80070017);

    private readonly IFileSystem _fs;
    private readonly ILogger _log;
    private readonly int _oemCodePage;
    private readonly Func<string, ArchivePath?> _parse;
    private readonly object _gate = new();
    private readonly Dictionary<string, Verdict> _verdicts = new(StringComparer.OrdinalIgnoreCase);


    /// <param name="oemCodePage">The machine's OEM code page, for names not flagged UTF-8 (<see cref="ZipCatalog"/>).</param>
    /// <param name="parse">The archive / inner split of a path - <see cref="IShellNamespace.ParseArchive"/>.</param>
    public ZipFallback(IFileSystem fs, ILogger log, int oemCodePage, Func<string, ArchivePath?> parse) {
        _fs = fs;
        _log = log;
        _oemCodePage = oemCodePage;
        _parse = parse;
    }


    /// <summary>True when the archive of <paramref name="path"/> is read here rather than by the shell.</summary>
    public bool Owns(ArchivePath path) => CatalogOf(path) is not null;

    /// <summary>
    /// What to list at <paramref name="path"/>, given what the shell listed
    /// there: the shell's rows, unless they are none and the zip's own
    /// directory says otherwise - then the archive becomes Wander's.
    /// </summary>
    public IReadOnlyList<FileSystemEntry> Listed(ArchivePath path, IReadOnlyList<FileSystemEntry> shellRows, CancellationToken ct) {
        if (!IsZip(path) || StampOf(path.Archive) is not { } stamp) {
            return shellRows;
        }

        lock (_gate) {
            if (_verdicts.TryGetValue(path.Archive, out var known) && known.Stamp == stamp && known.Catalog is null) {
                return shellRows;
            }
        }
        if (shellRows.Count > 0) {
            Remember(path.Archive, new Verdict(stamp, null));

            return shellRows;
        }

        ZipCatalog catalog;
        try {
            using var stream = _fs.OpenRead(path.Archive);
            catalog = ZipCatalog.Read(stream, _oemCodePage, ct);
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
            _log.Info($"Zip fallback: directory not read: {path.Archive} ({ex.Message})");

            return shellRows;
        }

        var rows = catalog.List(path.Archive, path.Inner);
        if (rows is not { Count: > 0 }) {
            Remember(path.Archive, new Verdict(stamp, null));

            return shellRows;
        }

        Remember(path.Archive, new Verdict(stamp, catalog));
        _log.Info($"Zip fallback: the shell listed nothing in {path.Archive}, its directory holds {catalog.FileCount} files - read by Wander");

        return rows;
    }

    /// <summary>One level of an archive this class <see cref="Owns"/>; empty when there is no such folder.</summary>
    public IReadOnlyList<FileSystemEntry> Enumerate(ArchivePath path) {
        return CatalogOf(path)?.List(path.Archive, path.Inner) ?? Array.Empty<FileSystemEntry>();
    }

    /// <summary>True when <paramref name="path"/> is a folder of an archive this class <see cref="Owns"/>.</summary>
    public bool IsFolder(ArchivePath path) => CatalogOf(path)?.IsFolder(path.Inner) == true;

    /// <summary>The unpacked size of a file in an archive this class <see cref="Owns"/>; null for anything else.</summary>
    public long? SizeOf(ArchivePath path) => CatalogOf(path)?.SizeOf(path.Inner);

    /// <summary>
    /// The bytes of a file in an archive this class <see cref="Owns"/>,
    /// checked against its CRC; null when it is not such a file, is
    /// encrypted, or does not read back whole.
    /// </summary>
    public byte[]? ReadEntry(ArchivePath path) {
        try {
            using var stream = _fs.OpenRead(path.Archive);
            using var zip = ZipCatalog.Open(stream, _oemCodePage);
            if (FileEntry(zip, path.Inner) is not { IsEncrypted: false } entry) {
                return null;
            }

            using var body = new CrcCheckedStream(entry.Open(), entry.Crc32, entry.Length);
            var bytes = new MemoryStream();
            body.CopyTo(bytes);

            return bytes.ToArray();
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
            _log.Info($"Zip fallback: entry not read: {path.Archive}\\{path.Inner} ({ex.Message})");

            return null;
        }
    }

    /// <summary>
    /// Unpacks <paramref name="items"/> - files and folders of archives
    /// this class <see cref="Owns"/> - into <paramref name="targetFolder"/>,
    /// the contract of <see cref="IShellNamespace.CopyOut"/>: the way is
    /// clear, a new name is taken when given, every file landed is reported
    /// by its path in the archive, and a folder by its own once whole.
    /// A file is written under a temporary name and takes its own only
    /// when its CRC agrees (<see cref="IFileSystem.WriteFromStream"/>).
    /// </summary>
    public void CopyOut(IReadOnlyList<CopyOutItem> items, string targetFolder, IProgress<string>? progress, CancellationToken ct) {
        foreach (var batch in items.GroupBy(item => ArchiveOf(item.Path).Archive, StringComparer.OrdinalIgnoreCase)) {
            using var stream = _fs.OpenRead(batch.Key);
            using var zip = ZipCatalog.Open(stream, _oemCodePage);
            foreach (var item in batch) {
                ct.ThrowIfCancellationRequested();
                var source = ArchiveOf(item.Path);
                string destination = Path.Combine(targetFolder, item.NewName ?? Path.GetFileName(source.Inner));
                if (FileEntry(zip, source.Inner) is { } file) {
                    Write(file, destination, ct);
                } else {
                    CopyFolder(zip, source, destination, progress, ct);
                }
                progress?.Report(item.Path);
            }
        }
    }


    private ArchivePath ArchiveOf(string path) {
        return _parse(path) ?? throw new FileNotFoundException("Not a path inside an archive", path);
    }

    /// <summary>
    /// Everything under the folder <paramref name="source"/>, into
    /// <paramref name="destination"/>: its folders, empty ones too, and its
    /// files. A name Windows cannot hold is left behind, said in the log -
    /// the listing never showed it.
    /// </summary>
    private void CopyFolder(ZipArchive zip, ArchivePath source, string destination, IProgress<string>? progress, CancellationToken ct) {
        string prefix = source.Inner.Replace('\\', '/').Trim('/') + "/";
        var entries = zip.Entries.Where(e => ZipCatalog.PathOf(e).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0 && CatalogOf(source)?.IsFolder(source.Inner) != true) {
            throw new FileNotFoundException("Not found inside the archive", Path.Combine(source.Archive, source.Inner));
        }

        _fs.CreateDirectory(destination);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (var entry in entries) {
            ct.ThrowIfCancellationRequested();
            string relative = ZipCatalog.PathOf(entry)[prefix.Length..];
            string[] names = relative.Split('/');
            if (relative.Length == 0 || !names.All(ZipCatalog.IsPlainName)) {
                skipped++;
                continue;
            }
            // A zip may hold the same name twice; the shell takes the first.
            if (!done.Add(relative)) {
                continue;
            }

            string target = Path.Combine(destination, Path.Combine(names));
            bool isFolder = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            _fs.CreateDirectory(isFolder ? target : Path.GetDirectoryName(target)!);
            if (!isFolder) {
                Write(entry, target, ct);
                progress?.Report(Path.Combine(source.Archive, source.Inner, Path.Combine(names)));
            }
        }

        if (skipped > 0) {
            _log.Warn($"Zip fallback: {skipped} entries under {source.Archive}\\{source.Inner} not extracted - names Windows cannot hold");
        }
    }

    private void Write(ZipArchiveEntry entry, string destination, CancellationToken ct) {
        if (entry.IsEncrypted) {
            throw new ArchiveLockedException();
        }

        using var body = new CrcCheckedStream(entry.Open(), entry.Crc32, entry.Length);
        _fs.WriteFromStream(body, entry.Length, destination, entry.LastWriteTime.UtcDateTime, null, ct);
    }

    /// <summary>The first file entry at <paramref name="inner"/>; null when there is none - a folder, or nothing.</summary>
    private static ZipArchiveEntry? FileEntry(ZipArchive zip, string inner) {
        string path = inner.Replace('\\', '/').Trim('/');

        return zip.Entries.FirstOrDefault(e =>
            !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\')
            && string.Equals(ZipCatalog.PathOf(e), path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The directory of an archive this class owns, while the file on disk is still the one it was read from.</summary>
    private ZipCatalog? CatalogOf(ArchivePath path) {
        Verdict? known;
        lock (_gate) {
            if (!_verdicts.TryGetValue(path.Archive, out known) || known.Catalog is null) {
                return null;
            }
        }
        if (StampOf(path.Archive) == known.Stamp) {
            return known.Catalog;
        }

        lock (_gate) {
            _verdicts.Remove(path.Archive);
        }

        return null;
    }

    private void Remember(string archive, Verdict verdict) {
        lock (_gate) {
            _verdicts[archive] = verdict;
        }
    }

    private (long Length, DateTime ModifiedUtc)? StampOf(string archive) {
        return _fs.GetEntry(archive) is { Kind: EntryKind.File, Size: { } length } file ? (length, file.ModifiedUtc) : null;
    }

    private static bool IsZip(ArchivePath path) {
        return string.Equals(Path.GetExtension(path.Archive), ".zip", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>What is known of one archive: read by the shell (no catalog), or by Wander from this catalog.</summary>
    private sealed record Verdict((long Length, DateTime ModifiedUtc) Stamp, ZipCatalog? Catalog);


    /// <summary>
    /// An entry's bytes, checked as they go by against the CRC-32 and the
    /// length its directory states. The end of the stream is where the
    /// verdict falls: a mismatch throws instead of reporting the end, so a
    /// copy loop that reads to the end never finishes a damaged file.
    /// </summary>
    private sealed class CrcCheckedStream : Stream {
        private static readonly uint[] _table = BuildTable();

        private readonly Stream _inner;
        private readonly uint _expectedCrc;
        private readonly long _expectedLength;
        private uint _crc = 0xFFFFFFFF;
        private long _read;


        public CrcCheckedStream(Stream inner, uint expectedCrc, long expectedLength) {
            _inner = inner;
            _expectedCrc = expectedCrc;
            _expectedLength = expectedLength;
        }


        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _expectedLength;

        public override long Position {
            get => _read;
            set => throw new NotSupportedException();
        }


        public override int Read(byte[] buffer, int offset, int count) {
            int read = _inner.Read(buffer, offset, count);
            for (int i = offset; i < offset + read; i++) {
                _crc = _table[(_crc ^ buffer[i]) & 0xFF] ^ (_crc >> 8);
            }
            _read += read;

            if (read == 0 && count > 0 && (_read != _expectedLength || ~_crc != _expectedCrc)) {
                throw new IOException("The archive entry is damaged: its CRC does not match", CrcError);
            }

            return read;
        }

        public override void Flush() {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();


        protected override void Dispose(bool disposing) {
            if (disposing) {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }


        private static uint[] BuildTable() {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++) {
                uint c = n;
                for (int k = 0; k < 8; k++) {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }

            return table;
        }
    }
}
