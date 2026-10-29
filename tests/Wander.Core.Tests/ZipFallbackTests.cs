using System.IO.Compression;
using System.Text;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Shell;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

/// <summary>A zip the shell lists as empty, read by Wander instead.</summary>
public class ZipFallbackTests {
    private const string Archive = @"E:\in\shot.zip";

    private static readonly HashSet<string> _zipOnly = new() { ".zip" };

    private readonly FakeFileSystem _fs = new();
    private readonly ZipFallback _zips;


    public ZipFallbackTests() {
        _zips = new ZipFallback(_fs, NullLogger.Instance, 866, path => ArchivePath.Parse(path, _zipOnly));
    }


    [Fact]
    public void RowsFromTheShell_StayTheShells() {
        Put(("shot/a.MOV", "aaa"));
        var shellRows = new[] { Row(@"E:\in\shot.zip\shot", EntryKind.Directory) };

        var rows = _zips.Listed(At(""), shellRows, default);

        Assert.Same(shellRows, rows);
        Assert.False(_zips.Owns(At("")));
    }


    [Fact]
    public void NothingFromTheShell_ButEntriesInTheDirectory_TheArchiveIsWanders() {
        Put(("shot/", ""), ("shot/a.MOV", "aaa"), ("shot/unused/b.MOV", "bb"));

        var root = _zips.Listed(At(""), Array.Empty<FileSystemEntry>(), default);

        var folder = Assert.Single(root);
        Assert.Equal(("shot", @"E:\in\shot.zip\shot", EntryKind.Directory), (folder.Name, folder.FullPath, folder.Kind));
        Assert.True(_zips.Owns(At(@"shot\a.MOV")));
        Assert.True(_zips.IsFolder(At(@"shot\unused")));
        Assert.False(_zips.IsFolder(At(@"shot\a.MOV")));
        Assert.Equal(3, _zips.SizeOf(At(@"shot\a.MOV")));

        var level = _zips.Enumerate(At("shot")).OrderBy(e => e.Name).ToList();
        Assert.Equal(new[] { "a.MOV", "unused" }, level.Select(e => e.Name));
        Assert.Equal(new long?[] { 3, null }, level.Select(e => e.Size));
    }


    [Fact]
    public void AnEmptyZip_StaysEmpty_AndTheShells() {
        Put();

        var rows = _zips.Listed(At(""), Array.Empty<FileSystemEntry>(), default);

        Assert.Empty(rows);
        Assert.False(_zips.Owns(At("")));
    }


    [Fact]
    public void AZipDownloadedAgain_IsAskedAboutAgain() {
        Put(("a.txt", "x"));
        _zips.Listed(At(""), Array.Empty<FileSystemEntry>(), default);

        _fs.ModifiedTimes[Archive] = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(_zips.Owns(At("")));
    }


    [Fact]
    public void ANameOutOfTheFolder_IsNeitherListedNorExtracted() {
        Put(("shot/a.txt", "a"), ("shot/../../evil.txt", "e"), ("shot/c:d.txt", "c"));
        Own();

        Assert.Equal(new[] { "a.txt" }, _zips.Enumerate(At("shot")).Select(e => e.Name));

        _fs.Directories.Add(@"D:\out");
        _zips.CopyOut(new[] { new CopyOutItem(@"E:\in\shot.zip\shot") }, @"D:\out", null, default);

        Assert.Equal(new[] { @"D:\out\shot\a.txt" }, _fs.Files.Keys.Where(k => k.StartsWith(@"D:\out", StringComparison.Ordinal)));
    }


    [Fact]
    public void AFolder_ComesOutWhole_EveryFileReportedAndTheFolderLast() {
        Put(("shot/a.MOV", "aaa"), ("shot/empty/", ""), ("shot/unused/b.MOV", "bb"));
        Own();
        _fs.Directories.Add(@"D:\out");
        var landed = new List<string>();

        _zips.CopyOut(new[] { new CopyOutItem(@"E:\in\shot.zip\shot", "shot (2)") }, @"D:\out", new Inline(landed), default);

        Assert.Equal("aaa", Encoding.ASCII.GetString(_fs.Files[@"D:\out\shot (2)\a.MOV"]));
        Assert.Equal("bb", Encoding.ASCII.GetString(_fs.Files[@"D:\out\shot (2)\unused\b.MOV"]));
        Assert.Contains(@"D:\out\shot (2)\empty", _fs.Directories);
        Assert.Equal(
            new[] { @"E:\in\shot.zip\shot\a.MOV", @"E:\in\shot.zip\shot\unused\b.MOV", @"E:\in\shot.zip\shot" },
            landed);
    }


    [Fact]
    public void ADamagedEntry_FailsWithACrcError_AndLeavesNothing() {
        Put(("a.txt", "hello"));
        int at = _fs.Files[Archive].AsSpan().IndexOf("hello"u8);
        _fs.Files[Archive][at] = (byte)'j';
        Own();
        _fs.Directories.Add(@"D:\out");

        var ex = Assert.Throws<IOException>(() =>
            _zips.CopyOut(new[] { new CopyOutItem(@"E:\in\shot.zip\a.txt") }, @"D:\out", null, default));

        Assert.Equal(23, TransientIo.Win32Code(ex));
        Assert.False(_fs.FileExists(@"D:\out\a.txt"));
        Assert.Null(_zips.ReadEntry(At("a.txt")));
    }


    [Fact]
    public void AnEncryptedEntry_IsLocked_NotItsCipherText() {
        Put(("a.txt", "hello"));
        MarkEncrypted(_fs.Files[Archive]);
        Own();
        _fs.Directories.Add(@"D:\out");

        Assert.Throws<ArchiveLockedException>(() =>
            _zips.CopyOut(new[] { new CopyOutItem(@"E:\in\shot.zip\a.txt") }, @"D:\out", null, default));
        Assert.Null(_zips.ReadEntry(At("a.txt")));
    }


    [Fact]
    public void AnEntryReadWhole_IsItsBytes() {
        Put(("shot/a.txt", "hello"));
        Own();

        Assert.Equal("hello", Encoding.ASCII.GetString(_zips.ReadEntry(At(@"shot\a.txt"))!));
        Assert.Null(_zips.ReadEntry(At("shot")));
    }


    private static ArchivePath At(string inner) => new(Archive, inner);

    /// <summary>The archive made Wander's, the way the shell's empty listing makes it.</summary>
    private void Own() {
        _zips.Listed(At(""), Array.Empty<FileSystemEntry>(), default);
        Assert.True(_zips.Owns(At("")));
    }

    private void Put(params (string Name, string Body)[] entries) {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (name, body) in entries) {
                using var writer = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                writer.Write(Encoding.ASCII.GetBytes(body));
            }
        }
        _fs.Files[Archive] = stream.ToArray();
        _fs.ModifiedTimes[Archive] = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>Sets bit 0 of the flags in every local header and directory record - what a password does.</summary>
    private static void MarkEncrypted(byte[] zip) {
        for (int i = 0; i + 4 <= zip.Length; i++) {
            if (zip[i] == 'P' && zip[i + 1] == 'K' && zip[i + 2] == 3 && zip[i + 3] == 4) {
                zip[i + 6] |= 1;
            } else if (zip[i] == 'P' && zip[i + 1] == 'K' && zip[i + 2] == 1 && zip[i + 3] == 2) {
                zip[i + 8] |= 1;
            }
        }
    }

    private static FileSystemEntry Row(string path, EntryKind kind) {
        return new FileSystemEntry(Path.GetFileName(path), path, kind, null, DateTime.MinValue, false, false, false, false);
    }


    private sealed class Inline : IProgress<string> {
        private readonly List<string> _landed;

        public Inline(List<string> landed) {
            _landed = landed;
        }

        public void Report(string value) => _landed.Add(value);
    }
}
