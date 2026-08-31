using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class FolderStatisticsTests {
    private const string Root = @"C:\root";


    private static FakeFileSystem Tree() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Root);
        fs.Directories.Add(@"C:\root\sub");
        fs.Directories.Add(@"C:\root\sub\deep");
        fs.Files[@"C:\root\a.txt"] = new byte[10];
        fs.Files[@"C:\root\b.txt"] = new byte[20];
        fs.Files[@"C:\root\photo.JPG"] = new byte[500];
        fs.Files[@"C:\root\sub\c.txt"] = new byte[5];
        fs.Files[@"C:\root\sub\deep\raw.cr2"] = new byte[1000];
        fs.Files[@"C:\root\README"] = new byte[3];

        return fs;
    }


    [Fact]
    public void Collect_CountsFilesAndFoldersRecursively() {
        var stats = FolderStatistics.Collect(Tree(), Root);

        Assert.Equal(6, stats.Files);
        Assert.Equal(2, stats.Folders);
        Assert.Equal(10 + 20 + 500 + 5 + 1000 + 3, stats.TotalSize);
        Assert.False(stats.Truncated);
    }

    [Fact]
    public void Collect_GroupsByExtension_CaseInsensitively() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Root);
        fs.Files[@"C:\root\one.JPG"] = new byte[100];
        fs.Files[@"C:\root\two.jpg"] = new byte[50];

        var stats = FolderStatistics.Collect(fs, Root);

        var jpg = Assert.Single(stats.Types);
        Assert.Equal("jpg", jpg.Extension);
        Assert.Equal(2, jpg.Count);
        Assert.Equal(150, jpg.Size);
    }

    [Fact]
    public void Collect_OrdersTypesByTotalSize() {
        var stats = FolderStatistics.Collect(Tree(), Root);

        // cr2 (1000) > jpg (500) > txt (35) > no extension (3).
        Assert.Equal(new[] { "cr2", "jpg", "txt", "—" }, stats.Types.Select(t => t.Extension));
    }

    [Fact]
    public void Collect_FileWithoutExtension_GoesToItsOwnBucket() {
        var stats = FolderStatistics.Collect(Tree(), Root);

        var none = stats.Types.Single(t => t.Extension == "—");
        Assert.Equal(1, none.Count);
        Assert.Equal(3, none.Size);
    }

    [Fact]
    public void Collect_DotFile_CountsAsExtensionless() {
        // ".gitignore" reads as a name, not as a "gitignore file" — whatever
        // Path.GetExtension says about it.
        var fs = new FakeFileSystem();
        fs.Directories.Add(Root);
        fs.Files[@"C:\root\.gitignore"] = new byte[7];

        var stats = FolderStatistics.Collect(fs, Root);

        Assert.Equal("—", Assert.Single(stats.Types).Extension);
    }

    [Fact]
    public void Collect_TrailingDot_CountsAsExtensionless() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Root);
        fs.Files[@"C:\root\weird."] = new byte[1];

        var stats = FolderStatistics.Collect(fs, Root);

        Assert.Equal("—", Assert.Single(stats.Types).Extension);
    }

    [Fact]
    public void Collect_KeepsOnlyTheRequestedNumberOfTypes() {
        var stats = FolderStatistics.Collect(Tree(), Root, maxTypes: 2);

        Assert.Equal(2, stats.Types.Count);
        // The totals still describe the whole folder, not just the two
        // buckets that made the cut.
        Assert.Equal(6, stats.Files);
    }

    [Fact]
    public void Collect_StopsOnFileBudget_AndSaysSo() {
        var stats = FolderStatistics.Collect(Tree(), Root, fileBudget: 3);

        Assert.True(stats.Truncated);
        Assert.Equal(3, stats.Files);
    }

    [Fact]
    public void Collect_EmptyFolder_IsAllZeroes() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Root);

        var stats = FolderStatistics.Collect(fs, Root);

        Assert.Equal(0, stats.Files);
        Assert.Equal(0, stats.Folders);
        Assert.Equal(0, stats.TotalSize);
        Assert.Empty(stats.Types);
        Assert.False(stats.Truncated);
    }

    [Fact]
    public void Collect_UnreadableSubtree_IsSkipped_NotFatal() {
        var fs = new ThrowingFileSystem(@"C:\root\sub");
        fs.Directories.Add(Root);
        fs.Directories.Add(@"C:\root\sub");
        fs.Files[@"C:\root\a.txt"] = new byte[10];

        var stats = FolderStatistics.Collect(fs, Root);

        // The folder itself is still counted; only its contents are lost.
        Assert.Equal(1, stats.Files);
        Assert.Equal(1, stats.Folders);
    }

    [Fact]
    public void Collect_StopsAtMaxDepth_AndSaysSo() {
        var fs = new FakeFileSystem();
        string path = Root;
        fs.Directories.Add(path);
        for (int i = 0; i < 6; i++) {
            path += @"\deeper";
            fs.Directories.Add(path);
        }
        fs.Files[path + @"\buried.txt"] = new byte[9];

        var stats = FolderStatistics.Collect(fs, Root, maxDepth: 3);

        Assert.True(stats.Truncated);
        // The file lives below the cut, so it is not counted...
        Assert.Equal(0, stats.Files);
        // ...but every folder met on the way is, cut-off ones included.
        Assert.Equal(4, stats.Folders);
    }

    [Fact]
    public void Collect_ReparsePointLoop_Terminates() {
        // A junction pointing at its own ancestor hands out an endlessly
        // deeper chain of *distinct* paths, so a "have I been here before"
        // set would never fire — only the depth cap ends this walk.
        var fs = new LoopingFileSystem();
        fs.Directories.Add(Root);

        var stats = FolderStatistics.Collect(fs, Root, maxDepth: 8);

        Assert.True(stats.Truncated);
        // Eight levels entered plus the ninth folder we saw and refused to
        // open. Without the cap this call never returns at all.
        Assert.Equal(9, stats.Folders);
    }

    [Fact]
    public void Collect_StopsOnFolderBudget_AndSaysSo() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Root);
        for (int i = 0; i < 20; i++) {
            fs.Directories.Add(Root + @"\sub" + i);
        }

        var stats = FolderStatistics.Collect(fs, Root, folderBudget: 5);

        Assert.True(stats.Truncated);
    }

    [Fact]
    public void Collect_ReportsRunningTotals() {
        var seen = new List<FolderProgress>();

        var stats = FolderStatistics.Collect(Tree(), Root, progress: new Recorder(seen));

        // A walk always says something: the first report goes out on the
        // first folder, so even a fast tree is not silent.
        Assert.NotEmpty(seen);
        // And it never claims more than it ended up with.
        Assert.All(seen, p => {
            Assert.True(p.Files <= stats.Files);
            Assert.True(p.Folders <= stats.Folders);
            Assert.True(p.TotalSize <= stats.TotalSize);
        });
    }

    [Fact]
    public void Collect_ByDefault_WalksToTheEnd() {
        // No file or folder budget any more: a wide folder is counted
        // whole, not up to a ceiling that then has to be apologised for.
        var fs = new FakeFileSystem();
        fs.Directories.Add(Root);
        for (int i = 0; i < 500; i++) {
            fs.Files[Root + @"\f" + i + ".txt"] = new byte[1];
        }

        var stats = FolderStatistics.Collect(fs, Root);

        Assert.Equal(500, stats.Files);
        Assert.False(stats.Truncated);
    }

    [Fact]
    public void Collect_HonoursCancellation() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => FolderStatistics.Collect(Tree(), Root, ct: cts.Token));
    }


    /// <summary>
    /// Every folder contains one subfolder, for ever — what a junction
    /// pointing at its own ancestor looks like from the outside.
    /// </summary>
    private sealed class LoopingFileSystem : FakeFileSystem {
        public override IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null) {
            return new[] {
                new FileSystemEntry(
                    Name: "loop",
                    FullPath: path + @"\loop",
                    Kind: EntryKind.Directory,
                    Size: null,
                    ModifiedUtc: DateTime.MinValue,
                    IsHidden: false,
                    IsReadOnly: false,
                    IsSystem: false,
                    LinksToDirectory: false),
            };
        }
    }


    /// <summary>
    /// Records progress reports on the calling thread. Not
    /// <see cref="Progress{T}"/>: that one posts to a synchronization
    /// context, and a test has none to post to.
    /// </summary>
    private sealed class Recorder : IProgress<FolderProgress> {
        private readonly List<FolderProgress> _seen;

        public Recorder(List<FolderProgress> seen) {
            _seen = seen;
        }

        public void Report(FolderProgress value) {
            _seen.Add(value);
        }
    }


    /// <summary>A file system that refuses to list one particular folder.</summary>
    private sealed class ThrowingFileSystem : FakeFileSystem {
        private readonly string _forbidden;

        public ThrowingFileSystem(string forbidden) {
            _forbidden = forbidden;
        }

        public override IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null) {
            return Throw(path) ? throw new UnauthorizedAccessException(path) : base.Enumerate(path, sort);
        }

        private bool Throw(string path) {
            return string.Equals(path, _forbidden, StringComparison.OrdinalIgnoreCase);
        }
    }
}
