using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class PreviewPairTests {
    private static readonly FileSystemEntry _a = File("a.jpg");
    private static readonly FileSystemEntry _b = File("b.cr3");
    private static readonly FileSystemEntry _c = File("c.txt");
    private static readonly FileSystemEntry[] _listing = { _a, _b, _c };


    [Fact]
    public void TwoPictures_PairInListingOrderWhicheverWasClickedFirst() {
        var pair = PreviewPair.Of(new[] { _b, _a }, _listing);

        Assert.NotNull(pair);
        Assert.Same(_a, pair!.Value.First);
        Assert.Same(_b, pair.Value.Second);
    }

    [Fact]
    public void OneOrThreeFiles_NoPair() {
        Assert.Null(PreviewPair.Of(new[] { _a }, _listing));
        Assert.Null(PreviewPair.Of(new[] { _a, _b, _c }, _listing));
    }

    [Fact]
    public void AFolderInThePair_NoPair() {
        var folder = new FileSystemEntry("photos", @"D:\photos", EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);

        Assert.Null(PreviewPair.Of(new[] { _a, folder }, _listing));
    }

    [Fact]
    public void AFileThePaneCannotDraw_NoPair() {
        Assert.Null(PreviewPair.Of(new[] { _a, File("data.bin") }, _listing));
    }

    [Fact]
    public void ARowNotInTheListing_GoesSecond() {
        var elsewhere = File("z.png");
        var pair = PreviewPair.Of(new[] { elsewhere, _c }, _listing);

        Assert.Same(_c, pair!.Value.First);
        Assert.Same(elsewhere, pair.Value.Second);
    }


    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, @"D:\photos\" + name, EntryKind.File, 1, DateTime.MinValue, false, false, false, false);
    }
}
