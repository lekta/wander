using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class PreviewNeighborsTests {
    private static readonly FileSystemEntry _a = File("a.jpg");
    private static readonly FileSystemEntry _b = File("b.cr3");
    private static readonly FileSystemEntry _c = File("c.png");
    private static readonly FileSystemEntry _d = File("d.txt");
    private static readonly FileSystemEntry[] _listing = { _a, _b, _c, _d };


    [Fact]
    public void WalkingDown_BelowFirst() {
        var next = PreviewNeighbors.Of(_listing, _b, _a.FullPath);

        Assert.Equal(new[] { _c, _a }, next);
    }

    [Fact]
    public void WalkingUp_AboveFirst() {
        var next = PreviewNeighbors.Of(_listing, _b, _c.FullPath);

        Assert.Equal(new[] { _a, _c }, next);
    }

    [Fact]
    public void NoPrevious_BelowFirst() {
        Assert.Equal(new[] { _c, _a }, PreviewNeighbors.Of(_listing, _b, null));
    }

    [Fact]
    public void NotAPicture_Skipped() {
        Assert.Equal(new[] { _b }, PreviewNeighbors.Of(_listing, _c, _b.FullPath));
    }

    [Fact]
    public void EdgesAndStrangers_OnlyWhatExists() {
        Assert.Equal(new[] { _b }, PreviewNeighbors.Of(_listing, _a, null));
        Assert.Empty(PreviewNeighbors.Of(_listing, File("elsewhere.jpg"), null));
    }

    [Fact]
    public void AFolderBeside_Skipped() {
        var folder = new FileSystemEntry("x.jpg", @"D:\photos\x.jpg", EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);

        Assert.Equal(new[] { _a }, PreviewNeighbors.Of(new[] { _a, _b, folder }, _b, _a.FullPath));
    }


    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, @"D:\photos\" + name, EntryKind.File, 1, DateTime.MinValue, false, false, false, false);
    }
}
