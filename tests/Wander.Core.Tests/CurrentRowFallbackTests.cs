using Wander.Core.FileSystem;
using Wander.Core.Listing;

namespace Wander.Core.Tests;

public class CurrentRowFallbackTests {

    private static FileSystemEntry Row(string name) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\folder\" + name,
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }

    private static FileSystemEntry[] Rows(params string[] names) {
        return names.Select(Row).ToArray();
    }

    private static string[] Paths(params string[] names) {
        return names.Select(n => @"C:\folder\" + n).ToArray();
    }


    [Fact]
    public void RowGone_TheNextOneIsCurrent() {
        // Looking through photographs and throwing one out: the next
        // photograph is what comes up.
        var after = Rows("a", "b", "d");

        var next = CurrentRowFallback.After(Rows("a", "b", "c", "d"), Paths("c"), after);

        Assert.Same(after[2], next);
    }

    [Fact]
    public void LastRowGone_ThePreviousOneIsCurrent() {
        var after = Rows("a", "b");

        Assert.Same(after[1], CurrentRowFallback.After(Rows("a", "b", "c"), Paths("c"), after));
    }

    [Fact]
    public void SeveralGone_TheSearchStartsFromTheLastOfThem() {
        // The same answer Del gives for a multi-selection: what follows the
        // whole run, not what stood between its members.
        var after = Rows("a", "c", "e");

        Assert.Same(after[2], CurrentRowFallback.After(Rows("a", "b", "c", "d", "e"), Paths("b", "d"), after));
    }

    [Fact]
    public void NeighboursGoneToo_TheNearestSurvivorIsCurrent() {
        var after = Rows("a", "e");

        Assert.Same(after[1], CurrentRowFallback.After(Rows("a", "b", "c", "d", "e"), Paths("b"), after));
    }

    [Fact]
    public void NothingLeft_NoCurrentRow() {
        Assert.Null(CurrentRowFallback.After(Rows("a"), Paths("a"), Rows()));
    }

    [Fact]
    public void DepartedWasNotInTheFolder_NoCurrentRow() {
        Assert.Null(CurrentRowFallback.After(Rows("a", "b"), Paths("x"), Rows("a")));
    }

    [Fact]
    public void PathsMatchCaseInsensitively() {
        var after = new[] { Row("b") with { FullPath = @"C:\FOLDER\B" } };

        Assert.Same(after[0], CurrentRowFallback.After(Rows("a", "b"), new[] { @"C:\Folder\A" }, after));
    }
}
