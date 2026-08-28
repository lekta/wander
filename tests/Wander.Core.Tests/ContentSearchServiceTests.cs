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
        var (hits, outcome) = await Run(fs, ByName("report", SearchScope.CurrentFolder));

        Assert.Equal(new[] { @"C:\root\report.txt" }, Paths(hits));
        Assert.False(outcome.Truncated);
    }


    [Fact]
    public async Task Search_Subfolders_ReachesNestedFiles() {
        var fs = Tree();
        var (hits, _) = await Run(fs, ByName("report", SearchScope.Subfolders));

        Assert.Equal(
            new[] { @"C:\root\report.txt", @"C:\root\sub\old-report.txt" },
            Paths(hits).Order(StringComparer.Ordinal).ToArray());
    }


    [Fact]
    public async Task Search_MatchesOnTheNameOnly_NeverOnThePath() {
        // The folder is called tmp2; the file inside it is not. A search for
        // "2" must not drag the file in on its parent's name.
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Directories.Add(@"C:\root\tmp2");
        fs.Files[@"C:\root\tmp2\cat.webp"] = Text("x");

        var (hits, _) = await Run(fs, ByName("2", SearchScope.Subfolders));

        Assert.Equal(new[] { @"C:\root\tmp2" }, Paths(hits));
    }


    [Fact]
    public async Task Search_MatchesFolderNames() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Directories.Add(@"C:\root\reports");

        var (hits, _) = await Run(fs, ByName("report", SearchScope.CurrentFolder));

        Assert.Equal(new[] { @"C:\root\reports" }, Paths(hits));
    }


    [Fact]
    public async Task Search_WithText_DropsFolders() {
        // A folder has no contents; it cannot satisfy "contains this word".
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Directories.Add(@"C:\root\reports");
        fs.Files[@"C:\root\reports.txt"] = Text("quarterly report");

        var (hits, _) = await Run(fs, Request("report", "quarterly", SearchScope.CurrentFolder));

        Assert.Equal(new[] { @"C:\root\reports.txt" }, Paths(hits));
    }


    [Fact]
    public async Task Search_Wildcards_ReachTheService() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\Program.cs"] = Text("x");
        fs.Files[@"C:\root\Program.cs.bak"] = Text("x");
        fs.Files[@"C:\root\readme.md"] = Text("x");

        var (hits, _) = await Run(fs, ByName("*.cs", SearchScope.CurrentFolder));

        Assert.Equal(new[] { @"C:\root\Program.cs" }, Paths(hits));
    }


    [Fact]
    public async Task Search_HonoursVisibility() {
        var fs = new HiddenAwareFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\report.txt"] = Text("x");
        fs.Files[@"C:\root\report.hidden.txt"] = Text("x");
        fs.Hidden.Add(@"C:\root\report.hidden.txt");

        var request = ByName("report", SearchScope.CurrentFolder) with {
            Visibility = new EntryVisibility(ShowHidden: false, ShowSystem: false, HideSystemRootFolders: false),
        };
        var (hits, _) = await Run(fs, request);

        Assert.Equal(new[] { @"C:\root\report.txt" }, Paths(hits));
    }


    // --- Contents ------------------------------------------------------

    [Fact]
    public async Task Search_Text_FindsItInsideFiles() {
        var fs = Tree();
        var (hits, _) = await Run(fs, ByText("бюджет", SearchScope.Subfolders));

        var hit = Assert.Single(hits);
        Assert.Equal(@"C:\root\sub\notes.md", hit.Entry.FullPath);
        Assert.Equal(2, hit.Line);
        Assert.Contains("бюджет", hit.Snippet);
        // The snippet rides on the entry too, because that is what the list binds to.
        Assert.Equal(hit.Snippet, hit.Entry.MatchSnippet);
    }


    [Fact]
    public async Task Search_NameMatchAlone_IsNotAHit_WhenTextWasAsked() {
        // The regression this whole shape exists to prevent: a picture whose
        // name happens to contain the letter being looked for inside
        // documents used to land in the results.
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\budget.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 };
        fs.Files[@"C:\root\notes.md"] = Text("the budget is fine");

        var (hits, _) = await Run(fs, ByText("budget", SearchScope.CurrentFolder));

        Assert.Equal(new[] { @"C:\root\notes.md" }, Paths(hits));
    }


    [Fact]
    public async Task Search_TextMatchAlone_IsNotAHit_WhenTheMaskRejects() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\notes.md"] = Text("the budget is fine");
        fs.Files[@"C:\root\Program.cs"] = Text("// the budget is fine");

        var (hits, _) = await Run(fs, Request("*.cs", "budget", SearchScope.CurrentFolder));

        Assert.Equal(new[] { @"C:\root\Program.cs" }, Paths(hits));
    }


    [Fact]
    public async Task Search_MaskGatesTheRead_SoRejectedFilesAreNotScanned() {
        // "Every .cs that mentions X" must not open the .png next to it.
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\Program.cs"] = Text("nothing");
        for (int i = 0; i < 10; i++) {
            fs.Files[$@"C:\root\image{i}.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        }

        var (_, outcome) = await Run(fs, Request("*.cs", "budget", SearchScope.CurrentFolder));

        Assert.Equal(1, outcome.FilesScanned);
        Assert.Equal(0, outcome.UnreadableFiles);
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

        var (_, outcome) = await Run(fs, ByText("anything", SearchScope.CurrentFolder));

        Assert.Equal(1, outcome.UnreadableFiles);
        Assert.Equal(1, outcome.FilesScanned);
    }


    [Fact]
    public async Task Search_FailedDocumentFormat_DoesNotFallThroughToPlainText() {
        // A PDF has readable ASCII in its header, so the catch-all extractor
        // will happily call it text and match a query against
        // "%PDF-1.4 ReportLab Generated PDF". Once a format-specific
        // extractor has claimed the file and failed, that is the answer.
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\report.docx"] = Text("%PDF-1.4 ReportLab Generated PDF document");

        var service = new ContentSearchService(
            fs,
            new IContentExtractor[] { new ZipDocumentExtractor(fs), new PlainTextExtractor(fs) },
            new ExtractedTextCache());

        var hits = new List<SearchHit>();
        var outcome = await service.RunAsync(
            ByText("ReportLab", SearchScope.CurrentFolder), batch => hits.AddRange(batch), null, default);

        Assert.Empty(hits);
        Assert.Equal(1, outcome.UnreadableFiles);
    }


    [Fact]
    public async Task Search_DoesNotCountOrdinaryBinaries_AsUnreadable() {
        // A search through a source tree walks past thousands of .dll and
        // .png files. Counting those would turn the warning into noise that
        // hides the one case it exists for.
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\image.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01 };

        var (_, outcome) = await Run(fs, ByText("anything", SearchScope.CurrentFolder));

        Assert.Equal(0, outcome.UnreadableFiles);
        Assert.Equal(1, outcome.FilesScanned);
    }


    [Fact]
    public async Task Search_OversizedFile_IsNotOpened_AndDoesNotMatchOnNameAlone() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\huge-report.log"] = Text("needle");

        var request = Request("report", "needle", SearchScope.CurrentFolder) with { MaxFileSize = 1 };
        var (hits, _) = await Run(fs, request);

        Assert.Empty(hits);
    }


    [Fact]
    public async Task Search_OversizedFile_StillMatchesByNameAlone() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\huge-report.log"] = Text("needle");

        var request = ByName("report", SearchScope.CurrentFolder) with { MaxFileSize = 1 };
        var (hits, _) = await Run(fs, request);

        Assert.Single(hits);
    }


    // --- Binaries ------------------------------------------------------

    [Fact]
    public async Task Search_Binaries_AreSkippedByDefault() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\app.exe"] = Binary("FileVersion");

        var (hits, _) = await Run(fs, ByText("FileVersion", SearchScope.CurrentFolder));

        Assert.Empty(hits);
    }


    [Fact]
    public async Task Search_Binaries_AreScannedWhenAskedFor() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\app.exe"] = Binary("FileVersion");
        fs.Files[@"C:\root\other.exe"] = Binary("Something else");

        var request = ByText("fileversion", SearchScope.CurrentFolder) with { SearchBinaries = true };
        var (hits, _) = await Run(fs, request);

        var hit = Assert.Single(hits);
        Assert.Equal(@"C:\root\app.exe", hit.Entry.FullPath);
        // No line, no snippet: a binary has no lines.
        Assert.Null(hit.Snippet);
        Assert.Equal(0, hit.Line);
    }


    [Fact]
    public async Task Search_Binaries_StillObeyTheMask() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\app.exe"] = Binary("FileVersion");

        var request = Request("*.dll", "FileVersion", SearchScope.CurrentFolder) with { SearchBinaries = true };
        var (hits, _) = await Run(fs, request);

        Assert.Empty(hits);
    }


    // --- Limits and cancellation ---------------------------------------

    [Fact]
    public async Task Search_StopsAtMaxResults() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        for (int i = 0; i < 20; i++) {
            fs.Files[$@"C:\root\report{i}.txt"] = Text("x");
        }

        var request = ByName("report", SearchScope.CurrentFolder) with { MaxResults = 5 };
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

        var request = ByName("report", SearchScope.Subfolders) with { MaxDepth = 1 };
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
            () => service.RunAsync(ByName("report", SearchScope.Subfolders), _ => { }, null, cts.Token));
    }


    [Fact]
    public async Task Search_ReportsProgress() {
        var fs = Tree();
        var seen = new List<SearchProgress>();
        var progress = new SynchronousProgress(seen.Add);

        await Service(fs).RunAsync(ByText("о", SearchScope.Subfolders), _ => { }, progress, default);

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

        var (hits, _) = await Run(fs, ByName("report", SearchScope.Subfolders));

        Assert.Equal(new[] { @"C:\root\open\report.txt" }, Paths(hits));
    }


    [Fact]
    public async Task Search_EmptyRequest_IsRefused() {
        // "Everything, anywhere" is not a search. The controllers guard
        // against it; the service must not depend on them remembering.
        var fs = Tree();
        var (hits, outcome) = await Run(fs, Request("", "", SearchScope.Subfolders));

        Assert.Empty(hits);
        Assert.Equal(0, outcome.FilesScanned);
    }


    // --- Caching -------------------------------------------------------

    [Fact]
    public async Task Search_UsesCache_ForExpensiveExtractors() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\root");
        fs.Files[@"C:\root\a.fake"] = Text("needle");

        var cache = new ExtractedTextCache();
        var extractor = new CountingExtractor("needle here");
        var service = new ContentSearchService(fs, new IContentExtractor[] { extractor }, cache);
        var request = ByText("needle", SearchScope.CurrentFolder);

        await service.RunAsync(request, _ => { }, null, default);
        await service.RunAsync(request, _ => { }, null, default);

        Assert.Equal(1, extractor.Calls);
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


    private static SearchRequest Request(string name, string text, SearchScope scope) {
        return new SearchRequest(
            NameFilter.Parse(name), text, @"C:\root", scope, false, EntryVisibility.All);
    }


    private static SearchRequest ByName(string name, SearchScope scope) {
        return Request(name, "", scope);
    }


    private static SearchRequest ByText(string text, SearchScope scope) {
        return Request("", text, scope);
    }


    private static ContentSearchService Service(IFileSystem fs) {
        return new ContentSearchService(
            fs,
            new IContentExtractor[] { new ZipDocumentExtractor(fs), new PlainTextExtractor(fs) },
            new ExtractedTextCache());
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


    /// <summary>ASCII text wrapped in the NUL bytes that make a file binary.</summary>
    private static byte[] Binary(string embedded) {
        var bytes = new List<byte> { 0x00, 0x01, 0x02, 0x00 };
        bytes.AddRange(Encoding.ASCII.GetBytes(embedded));
        bytes.AddRange(new byte[] { 0x00, 0xFF, 0x00 });

        return bytes.ToArray();
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
