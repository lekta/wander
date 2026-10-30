using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Logging;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class FolderSizesTests {
    private const string A = @"C:\root\a";
    private const string B = @"C:\root\b";
    private const string Off = @"\\share\off";


    private readonly FakeFileSystem _fs = new();

    // The walks wait here until the test lets them go.
    private readonly Queue<Action> _walks = new();
    private readonly List<(string Path, long Size)> _measured = new();
    private int _drained;


    public FolderSizesTests() {
        _fs.Directories.Add(A);
        _fs.Directories.Add(B);
        _fs.Files[@"C:\root\a\one.bin"] = new byte[10];
        _fs.Directories.Add(@"C:\root\a\deep");
        _fs.Files[@"C:\root\a\deep\two.bin"] = new byte[5];
        _fs.Files[@"C:\root\b\three.bin"] = new byte[1];
    }


    [Fact]
    public void AFolderWanted_IsWalked_AndItsSizeKnown() {
        var sizes = Sizes();

        sizes.Want(A);
        Assert.Null(sizes.Known(A));
        RunWalks();

        Assert.Equal(15, sizes.Known(A));
        Assert.Equal((A, 15L), Assert.Single(_measured));
    }

    [Fact]
    public void WantedTwice_IsWalkedOnce() {
        var sizes = Sizes();

        sizes.Want(A);
        sizes.Want(A);

        Assert.Single(_walks);
    }

    [Fact]
    public void TheLastWanted_IsWalkedFirst_TheScreenAfterAScrollDoesNotWait() {
        var sizes = Sizes();
        _fs.Directories.Add(@"C:\root\c");
        _fs.Directories.Add(@"C:\root\d");

        foreach (string path in new[] { A, B, @"C:\root\c", @"C:\root\d" }) {
            sizes.Want(path);
        }
        RunWalks();

        // Two go at once, as they come; of the two that waited the later is first.
        Assert.Equal(new[] { A, B, @"C:\root\d", @"C:\root\c" }, _measured.Select(m => m.Path));
    }

    [Fact]
    public void WhatCannotBeWalked_HasNoAnswer() {
        var sizes = Sizes();

        _fs.EnumerateFailures.Add(B);

        sizes.Want(Off);
        sizes.Want(B);
        RunWalks();

        Assert.Null(sizes.Known(Off));
        Assert.Null(sizes.Known(B));
        Assert.Empty(_measured);
    }

    [Fact]
    public void Leaving_DropsWhatIsWalked_AndTheNextVisitWalksAgain() {
        var sizes = Sizes();
        sizes.Want(A);

        sizes.Leave();
        RunWalks();
        Assert.Null(sizes.Known(A));
        Assert.Empty(_measured);

        sizes.Want(A);
        RunWalks();
        Assert.Equal(15, sizes.Known(A));
    }

    [Fact]
    public void BackInTheFolder_TheNumberIsThereAtOnce_AndWalkedAgain() {
        var sizes = Sizes();
        sizes.Want(A);
        RunWalks();

        sizes.Leave();
        _fs.Files[@"C:\root\a\more.bin"] = new byte[100];

        Assert.Equal(15, sizes.Known(A));
        sizes.Want(A);
        RunWalks();
        Assert.Equal(115, sizes.Known(A));
    }

    [Fact]
    public void AskAgain_LetsEverythingBeWalkedAgain_AndSaysSo() {
        var sizes = Sizes();
        int told = 0;
        sizes.Invalidated += () => told++;
        sizes.Want(A);
        RunWalks();

        sizes.AskAgain();
        sizes.Want(A);

        Assert.Equal(1, told);
        Assert.Single(_walks);
    }

    [Theory]
    [InlineData(@"C:\root\a\deep\new.bin", true)] // Inside it.
    [InlineData(@"C:\root\a", true)] // It.
    [InlineData(@"C:\root", true)] // Above it.
    [InlineData(@"C:\root\b\x.bin", false)] // Beside it.
    public void AnOperation_HasTheFoldersItWentThroughWalkedAgain(string touched, bool again) {
        var sizes = Sizes();
        sizes.Want(A);
        RunWalks();

        sizes.AskAgainFor(new[] { touched });
        sizes.Want(A);

        Assert.Equal(again ? 1 : 0, _walks.Count);
    }

    [Fact]
    public void Drained_WhenTheLastAnswerIsIn_NotBefore() {
        var sizes = Sizes();
        sizes.Want(A);
        sizes.Want(B);

        _walks.Dequeue()();
        Assert.Equal(0, _drained);
        RunWalks();

        Assert.Equal(1, _drained);
    }

    [Fact]
    public void SwitchedOff_NothingIsWalkedOrKnown() {
        var sizes = Sizes();
        sizes.Want(A);
        RunWalks();

        sizes.IsOn = false;
        sizes.Want(B);

        Assert.Null(sizes.Known(A));
        Assert.Empty(_walks);
    }

    [Fact]
    public void SizedListing_PutsSizesOnFoldersOnly_AndLeavesTheListWhenNothingChanges() {
        var folder = new FileSystemEntry("a", A, EntryKind.Directory, null, default, false, false, false, false);
        var file = new FileSystemEntry("f.bin", @"C:\root\f.bin", EntryKind.File, 3, default, false, false, false, false);
        var rows = new[] { folder, file };

        var nothing = SizedListing.WithSizes(rows, _ => null);
        Assert.Same(rows, nothing);

        var sized = SizedListing.WithSizes(rows, _ => 15);
        Assert.Equal(15, sized[0].ContentSize);
        Assert.Same(file, sized[1]);
    }

    [Fact]
    public void OrderedBySize_AFolderGoesByWhatItHolds() {
        var small = new FileSystemEntry("a", A, EntryKind.Directory, null, default, false, false, false, false, ContentSize: 1);
        var big = new FileSystemEntry("b", B, EntryKind.Directory, null, default, false, false, false, false, ContentSize: 100);

        var sorted = EntryComparers.Sort(new[] { small, big }, new SortOptions(SortKey.Size, false, true));

        Assert.Same(big, sorted[0]);
    }

    [Theory]
    [InlineData(1000, 300, 698)]
    [InlineData(400, 300, 160)] // Not narrower than its minimum: the scroll bar then.
    public void TheNameColumn_TakesWhatTheOthersAndTheRowsFrameLeave(double viewport, double others, double width) {
        Assert.Equal(width, DetailsColumns.FillWidth(viewport, others));
    }


    private FolderSizes Sizes() {
        var sizes = new FolderSizes(_fs, path => !path.StartsWith(@"\\", StringComparison.Ordinal), post => post(), _walks.Enqueue, NullLogger.Instance);
        sizes.Measured += (path, size) => _measured.Add((path, size));
        sizes.Drained += () => _drained++;

        return sizes;
    }

    private void RunWalks() {
        while (_walks.TryDequeue(out var walk)) {
            walk();
        }
    }
}
