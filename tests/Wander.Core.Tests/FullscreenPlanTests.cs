using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class FullscreenPlanTests {
    private static readonly FileSystemEntry _a = File("a.jpg");
    private static readonly FileSystemEntry _b = File("b.cr3");
    private static readonly FileSystemEntry _c = File("c.gif");
    private static readonly FileSystemEntry _d = File("d.png");
    private static readonly FileSystemEntry _text = File("notes.txt");
    private static readonly FileSystemEntry[] _listing = { _a, _text, _b, _c, _d };


    [Fact]
    public void OnePicture_ShownAlone_TheListWalked() {
        var plan = FullscreenPlan.Of(new[] { _b }, _listing, _b.FullPath);

        Assert.NotNull(plan);
        Assert.Equal(FullscreenMode.Single, plan!.Mode);
        Assert.Same(_b, plan.Start);
    }

    [Fact]
    public void TwoPictures_APair_InTheListsOrder() {
        var plan = FullscreenPlan.Of(new[] { _c, _a }, _listing, _c.FullPath);

        Assert.Equal(FullscreenMode.Pair, plan!.Mode);
        Assert.Equal(new[] { _a, _c }, plan.Pictures);
        Assert.Same(_a, plan.Start);
    }

    [Fact]
    public void MorePictures_TheSelectionWalked_FromTheCaret() {
        var plan = FullscreenPlan.Of(new[] { _d, _a, _c }, _listing, _c.FullPath);

        Assert.Equal(FullscreenMode.Selection, plan!.Mode);
        Assert.Equal(new[] { _a, _c, _d }, plan.Pictures);
        Assert.Same(_c, plan.Start);
    }

    [Fact]
    public void TheCaretOutsideTheSelection_StartsAtTheFirst() {
        var plan = FullscreenPlan.Of(new[] { _d, _a, _c }, _listing, _b.FullPath);

        Assert.Same(_a, plan!.Start);
        Assert.Same(_a, FullscreenPlan.Of(new[] { _d, _a, _c }, _listing, null)!.Start);
    }

    /// <summary>Anything but pictures in the selection: Enter opens, as it always did.</summary>
    [Fact]
    public void NotAllPictures_NoPlan() {
        Assert.Null(FullscreenPlan.Of(new[] { _a, _text }, _listing, _a.FullPath));
        Assert.Null(FullscreenPlan.Of(new[] { _text }, _listing, _text.FullPath));
        Assert.Null(FullscreenPlan.Of(Array.Empty<FileSystemEntry>(), _listing, null));
    }

    [Fact]
    public void AFolder_NoPlan() {
        var folder = new FileSystemEntry("shots", @"D:\photos\shots", EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);

        Assert.Null(FullscreenPlan.Of(new[] { _a, folder }, _listing, _a.FullPath));
    }

    [Fact]
    public void ARowNotInTheListing_GoesLast() {
        var elsewhere = File("e.jpg");
        var plan = FullscreenPlan.Of(new[] { elsewhere, _d }, _listing, null);

        Assert.Equal(new[] { _d, elsewhere }, plan!.Pictures);
    }


    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, @"D:\photos\" + name, EntryKind.File, 1, DateTime.MinValue, false, false, false, false);
    }
}
