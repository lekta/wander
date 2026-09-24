using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class PictureWalkTests {
    private static readonly FileSystemEntry[] _rows = {
        File("notes.txt"), File("a.jpg"), File("b.cr3"), File("readme.md"), File("c.gif"), File("d.png"), File("zz.txt"),
    };


    [Fact]
    public void Forward_SkipsWhatIsNotAPicture() {
        Assert.Equal(4, PictureWalk.Step(_rows, Path("b.cr3"), 2, +1));
    }

    [Fact]
    public void Back_SkipsWhatIsNotAPicture() {
        Assert.Equal(2, PictureWalk.Step(_rows, Path("c.gif"), 4, -1));
    }

    [Fact]
    public void AtAnEnd_NowhereToGo() {
        Assert.Equal(-1, PictureWalk.Step(_rows, Path("d.png"), 5, +1));
        Assert.Equal(-1, PictureWalk.Step(_rows, Path("a.jpg"), 1, -1));
    }

    [Fact]
    public void FirstAndLast_ArePicturesNearestTheEnds() {
        Assert.Equal(1, PictureWalk.Step(_rows, Path("c.gif"), 4, PictureWalk.First));
        Assert.Equal(5, PictureWalk.Step(_rows, Path("a.jpg"), 1, PictureWalk.Last));
    }

    /// <summary>A star under the rating filter hid the picture on show: forward is the row that took its place.</summary>
    [Fact]
    public void GoneFromTheList_ForwardGoesToTheRowThatTookItsPlace() {
        var without = _rows.Where(r => r.Name != "b.cr3").ToArray();

        Assert.Equal(3, PictureWalk.Step(without, Path("b.cr3"), 2, +1));
    }

    [Fact]
    public void GoneFromTheList_BackGoesToTheRowBefore() {
        var without = _rows.Where(r => r.Name != "c.gif").ToArray();

        Assert.Equal(2, PictureWalk.Step(without, Path("c.gif"), 4, -1));
    }

    [Fact]
    public void GoneFromTheEndOfTheList_ForwardHasNowhereToGo() {
        var without = _rows.Where(r => r.Name != "d.png").ToArray();

        Assert.Equal(-1, PictureWalk.Step(without, Path("d.png"), 5, +1));
        Assert.Equal(4, PictureWalk.Step(without, Path("d.png"), 5, -1));
    }

    /// <summary>The right picture of a pair walks past the left one, both ways.</summary>
    [Fact]
    public void TheRightOfAPair_WalksPastTheLeft() {
        Assert.Equal(1, PictureWalk.Step(_rows, Path("c.gif"), 4, -1, skip: Path("b.cr3")));
        Assert.Equal(5, PictureWalk.Step(_rows, Path("b.cr3"), 2, +1, skip: Path("c.gif")));
    }

    [Fact]
    public void OnlyTheLeftThatWay_NowhereToGo() {
        Assert.Equal(-1, PictureWalk.Step(_rows, Path("b.cr3"), 2, -1, skip: Path("a.jpg")));
    }

    [Fact]
    public void AFolderIsNotAPicture() {
        var folder = new FileSystemEntry("shots.jpg", @"D:\photos\shots.jpg", EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);

        Assert.False(PictureWalk.IsPicture(folder));
        Assert.True(PictureWalk.IsPicture(File("c.gif")));
    }


    private static string Path(string name) {
        return @"D:\photos\" + name;
    }

    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, Path(name), EntryKind.File, 1, DateTime.MinValue, false, false, false, false);
    }
}
