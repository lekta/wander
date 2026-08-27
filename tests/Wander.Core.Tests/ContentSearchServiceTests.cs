using System.Text;
using Wander.Core.FileSystem;
using Wander.Core.Search;
using Wander.Core.Tests.Fakes;
using Xunit;

namespace Wander.Core.Tests;

public class ContentSearchServiceTests {
    // --- Names ---------------------------------------------------------

    [Fact]
    public async Task Search_MatchesNames_InCurrentFolderOnly() {
        var fs = Tree();
        var (hits, outcome) = await Run(fs, Request("report", SearchScope.CurrentFolder));

        Assert.Equal(new[] { @"C:\root\report.txt" }, Paths(hits));
        Assert.False(outcome.Truncated);
    }


    [Fact]
    public async Task Search_Subfolders_ReachesNestedFiles() {
        var fs = Tree();
        var (hits, _) = await Run(fs, Request("report", SearchScope.Subfolders));

        Assert.Equal(
            new[] { @"C:\root\report.txt", @"C:\root\sub\old-report.txt" },
            Paths(hits).Order(StringComparer.Ordinal).ToArray());
    }


    [Fact]
    public async Task Search_MatchesFolderNames() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Directories.Add(@"C:\root\reports");

        var (hits, _) = await Run(fs, Request("report", SearchScope.CurrentFolder));

        Assert.Equal(new[] { @"C:\root\reports" }, Paths(hits));
    }


    [Fact]
    public async Task Search_HonoursVisibility() {
        var fs = new HiddenAwareFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\report.txt"] = Text("x");
        fs.Files[@"C:\root\report.hidden.txt"] = Text("x");
        fs.Hidden.Add(@"C:\root\report.hidden.txt");

        var request = Request("report", SearchScope.CurrentFolder) with {
            Visibility = new EntryVisibility(ShowHidden: false, ShowSystem: false, HideSystemRootFolders: false),
        };
        var (hits, _) = await Run(fs, request);

        Assert.Equal(new[] { @"C:\root\report.txt" }, Paths(hits));
    }


    // --- Contents ------------------------------------------------------

    [Fact]
    public async Task Search_Contents_FindsTextInsideFiles() {
        var fs = Tree();
        var (hits, _) = await Run(fs, Request("бюджет", SearchScope.Subfolders, contents: true));

        var hit = Assert.Single(hits);
        Assert.Equal(@"C:\root\sub\notes.md", hit.Entry.FullPath);
        Assert.Equal(2, hit.Line);
        Assert.Contains("бюджет", hit.Snippet);
        // The snippet rides on the entry too, because that is what the list binds to.
        Assert.Equal(hit.Snippet, hit.Entry.MatchSnippet);
    }


    [Fact]
    public async Task Search_ContentsOff_DoesNotOpenFiles() {
        var fs = Tree();
        var (hits, _) = await Run(fs, Request("бюджет", SearchScope.Subfolders));

        Assert.Empty(hits);
    }


    [Fact]
    public async Task Search_NameMatchWins_WhenContentDoesNot() {
        // A file found by its name is found, whether or not its insides
        // could be read or matched.
        var fs = Tree();
        var (hits, _) = await Run(fs, Request("report", SearchScope.CurrentFolder, contents: true));

        var hit = Assert.Single(hits);
        Assert.Equal(@"C:\root\report.txt", hit.Entry.FullPath);
        Assert.Null(hit.Snippet);
    }


    [Fact]
    public async Task Search_CountsUnreadableDocuments() {
        // "No matches" and "nothing could be opened" are different answers
        // — a folder of PDFs without a PDF filter gives the second.
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        // Binary rather than merely not-a-zip: a .docx full of readable
        // text would fall through to the plain-text extractor and be read,
        // which is the right answer and not the case under test.
        fs.Files[@"C:\root\broken.docx"] = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x00, 0x02 };

        var (_, outcome) = await Run(fs, Request("anything", SearchScope.CurrentFolder, contents: true));

        Assert.Equal(1, outcome.UnreadableFiles);
        Assert.Equal(1, outcome.FilesScanned);
    }


    [Fact]
    public async Task Search_DoesNotCountOrdinaryBinaries_AsUnreadable() {
        // A search through a source tree walks past thousands of .dll and
        // .png files. Counting those would turn the warning into noise that
        // hides the one case it exists for.
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\image.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01 };

        var (_, outcome) = await Run(fs, Request("anything", SearchScope.CurrentFolder, contents: true));

        Assert.Equal(0, outcome.UnreadableFiles);
        Assert.Equal(1, outcome.FilesScanned);
    }


    [Fact]
    public async Task Search_OversizedFile_IsNotOpened_ButStillMatchesByName() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\huge-report.log"] = Text("needle");

        var request = Request("report", SearchScope.CurrentFolder, contents: true) with { MaxFileSize = 1 };
        var (hits, _) = await Run(fs, request);

        var hit = Assert.Single(hits);
        Assert.Null(hit.Snippet);
    }


    [Fact]
    public async Task Search_UsesCache_ForExpensiveExtractors() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\a.fake"] = Text("needle");

        var cache = new ExtractedTextCache();
        var extractor = new CountingExtractor("needle here");
        var service = new ContentSearchService(fs, new IContentExtractor[] { extractor }, cache);
        var request = Request("needle", SearchScope.CurrentFolder, contents: true);

        await service.RunAsync(request, _ => { }, null, default);
        await service.RunAsync(request, _ => { }, null, default);

        Assert.Equal(1, extractor.Calls);
    }


    // --- Limits and cancellation ---------------------------------------

    [Fact]
    public async Task Search_StopsAtMaxResults() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        for (int i = 0; i < 20; i++) {
            fs.Files[$@"C:\root\report{i}.txt"] = Text("x");
        }

        var request = Request("report", SearchScope.CurrentFolder) with { MaxResults = 5 };
        var (hits, outcome) = await Run(fs, request);

        Assert.Equal(5, hits.Count);
        Assert.Equal(5, outcome.Found);
        Assert.True(outcome.Truncated);
    }


    [Fact]
    public async Task Search_RespectsMaxDepth() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Directories.Add(@"C:\root\a");
        fs.Directories.Add(@"C:\root\a\b");
        fs.Files[@"C:\root\a\b\report.txt"] = Text("x");

        var request = Request("report", SearchScope.Subfolders) with { MaxDepth = 1 };
        var (hits, _) = await Run(fs, request);

        Assert.Empty(hits);
    }


    [Fact]
    public async Task Search_Cancelled_Throws() {
        var fs = Tree();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var service = Service(fs);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunAsync(Request("report", SearchScope.Subfolders), _ => { }, null, cts.Token));
    }


    [Fact]
    public async Task Search_ReportsProgress() {
        var fs = Tree();
        var seen = new List<SearchProgress>();
        var progress = new SynchronousProgress(seen.Add);

        await Service(fs).RunAsync(Request("report", SearchScope.Subfolders), _ => { }, progress, default);

        Assert.NotEmpty(seen);
        Assert.True(seen[^1].FilesScanned >= 3, $"scanned {seen[^1].FilesScanned}");
    }


    [Fact]
    public async Task Search_UnreadableFolder_IsSkipped_NotFatal() {
        var fs = new RefusingFileSystem(@"C:\root\locked");
        fs.Directories.Add(@"C:\root");
        fs.Directories.Add(@"C:\root\locked");
        fs.Directories.Add(@"C:\root\open");
        fs.Files[@"C:\root\open\report.txt"] = Text("x");

        var (hits, _) = await Run(fs, Request("report", SearchScope.Subfolders));

        Assert.Equal(new[] { @"C:\root\open\report.txt" }, Paths(hits));
    }


    // --- System index --------------------------------------------------

    [Fact]
    public async Task Search_Computer_UsesTheIndex() {
        var fs = Tree();
        var index = new FakeIndex(@"C:\root\report.txt", @"C:\gone\deleted.txt");
        var service = new ContentSearchService(
            fs,
            new IContentExtractor[] { new PlainTextExtractor(fs) },
            new ExtractedTextCache(),
            index);

        var hits = new List<SearchHit>();
        await service.RunAsync(
            Request("report", SearchScope.Computer),
            batch => hits.AddRange(batch),
            null,
            default);

        // The index outlives the files in it — the row that no longer
        // exists is dropped rather than shown as one that opens nothing.
        Assert.Equal(new[] { @"C:\root\report.txt" }, Paths(hits));
    }


    [Fact]
    public void CanSearchComputer_IsFalse_WithoutAnIndex() {
        Assert.False(Service(new FakeFileSystem()).CanSearchComputer);
    }


    // --- Scaffolding ---------------------------------------------------

    private static FakeFileSystem Tree() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Directories.Add(@"C:\root\sub");
        fs.Files[@"C:\root\report.txt"] = Text("nothing of interest");
        fs.Files[@"C:\root\sub\old-report.txt"] = Text("also nothing");
        fs.Files[@"C:\root\sub\notes.md"] = Text("первая строка\nсводный бюджет на год\nтретья");

        return fs;
    }


    private static SearchRequest Request(string query, SearchScope scope, bool contents = false) {
        return new SearchRequest(query, @"C:\root", scope, contents, EntryVisibility.All);
    }


    private static ContentSearchService Service(IFileSystem fs, IIndexedSearch? index = null) {
        return new ContentSearchService(
            fs,
            new IContentExtractor[] { new ZipDocumentExtractor(fs), new PlainTextExtractor(fs) },
            new ExtractedTextCache(),
            index);
    }


    private static async Task<(List<SearchHit> Hits, SearchOutcome Outcome)> Run(IFileSystem fs, SearchRequest request) {
        var hits = new List<SearchHit>();
        var outcome = await Service(fs).RunAsync(request, batch => hits.AddRange(batch), null, default);

        return (hits, outcome);
    }


    private static string[] Paths(IEnumerable<SearchHit> hits) {
        return hits.Select(h => h.Entry.FullPath).ToArray();
    }


    private static byte[] Text(string content) {
        return Encoding.UTF8.GetBytes(content);
    }


    /// <summary>A fake whose <c>Hidden</c> set drives <see cref="FileSystemEntry.IsHidden"/>.</summary>
    private sealed class HiddenAwareFileSystem : FakeFileSystem {
        public HashSet<string> Hidden { get; } = new(StringComparer.OrdinalIgnoreCase);


        public override IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null) {
            var entries = base.Enumerate(path, sort);
            var marked = new List<FileSystemEntry>(entries.Count);
            foreach (var entry in entries) {
                marked.Add(Hidden.Contains(entry.FullPath) ? entry with { IsHidden = true } : entry);
            }

            return marked;
        }
    }


    /// <summary>A fake with one folder that refuses to be listed.</summary>
    private sealed class RefusingFileSystem : FakeFileSystem {
        private readonly string _refused;


        public RefusingFileSystem(string refused) {
            _refused = refused;
        }


        public override IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null) {
            if (string.Equals(path, _refused, StringComparison.OrdinalIgnoreCase)) {
                throw new UnauthorizedAccessException(path);
            }

            return base.Enumerate(path, sort);
        }
    }


    /// <summary>Claims everything, counts how often it was actually asked.</summary>
    private sealed class CountingExtractor : IContentExtractor {
        private readonly string _text;


        public CountingExtractor(string text) {
            _text = text;
        }


        public int Calls { get; private set; }

        public bool IsExpensive => true;


        public bool CanExtract(string path) {
            return true;
        }


        public string? Extract(string path, CancellationToken token) {
            Calls++;

            return _text;
        }
    }


    private sealed class FakeIndex : IIndexedSearch {
        private readonly string[] _paths;


        public FakeIndex(params string[] paths) {
            _paths = paths;
        }


        public bool IsAvailable => true;


        public IReadOnlyList<string> Search(string query, string? scopePath, bool searchContents, int limit, CancellationToken token) {
            return _paths;
        }
    }


    /// <summary>
    /// <see cref="Progress{T}"/> posts to a synchronization context, which
    /// a test has none of — reports would arrive after the assertions.
    /// </summary>
    private sealed class SynchronousProgress : IProgress<SearchProgress> {
        private readonly Action<SearchProgress> _report;


        public SynchronousProgress(Action<SearchProgress> report) {
            _report = report;
        }


        public void Report(SearchProgress value) {
            _report(value);
        }
    }
}
