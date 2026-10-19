using Wander.Core.Navigation;

namespace Wander.Core.Tests;

public class BookmarkTitlesTests {
    private const string Dcim = @"device:Canon EOS R8\SD1\DCIM";
    private const string Reports = @"D:\Work\2026\Q3\final";


    [Fact]
    public void ABookmark_TakesItsOwnName() {
        var titles = new BookmarkTitles();

        Assert.True(titles.Set(Dcim, "  R8 DCIM ", "DCIM"));

        Assert.Equal("R8 DCIM", titles.Of(Dcim));
        Assert.Equal("R8 DCIM", titles.Of(Dcim + @"\"));
        Assert.Equal("R8 DCIM", titles.Of(@"DEVICE:canon eos r8\sd1\dcim"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("final")]
    public void ABlankName_OrTheFoldersOwn_GivesTheFolderBack(string? title) {
        var titles = new BookmarkTitles();
        titles.Set(Reports, "Отчёты", "final");

        Assert.True(titles.Set(Reports, title, "final"));

        Assert.Null(titles.Of(Reports));
        Assert.Empty(titles.All);
    }

    [Fact]
    public void TheSameNameAgain_ChangesNothing() {
        var titles = new BookmarkTitles();
        titles.Set(Reports, "Отчёты", "final");

        Assert.False(titles.Set(Reports, "Отчёты", "final"));
        Assert.False(titles.Set(@"D:\Elsewhere", "", "Elsewhere"));
    }

    [Fact]
    public void ANameGoesWhereItsFolderGoes() {
        var titles = new BookmarkTitles();
        titles.Set(Reports, "Отчёты", "final");
        titles.Set(@"D:\Work-old\x", "Не трогать", "x");

        // The folder above renamed by Wander: the bookmark follows, its name with it.
        Assert.True(titles.Follow(@"D:\Work", @"D:\Job"));

        Assert.Equal("Отчёты", titles.Of(@"D:\Job\2026\Q3\final"));
        Assert.Null(titles.Of(Reports));
        // A sibling with the same beginning is not inside.
        Assert.Equal("Не трогать", titles.Of(@"D:\Work-old\x"));
        Assert.False(titles.Follow(@"E:\Nothing", @"E:\Else"));
    }

    [Fact]
    public void ARemovedBookmark_ForgetsItsName() {
        var titles = new BookmarkTitles();
        titles.Set(Reports, "Отчёты", "final");

        Assert.True(titles.Forget(Reports));
        Assert.False(titles.Forget(Reports));
        Assert.Null(titles.Of(Reports));
    }

    [Fact]
    public void Loading_SkipsBlankNames() {
        var titles = new BookmarkTitles();

        titles.Load(new Dictionary<string, string> { [Reports] = " Отчёты ", [@"D:\x"] = "  " });

        Assert.Equal("Отчёты", titles.Of(Reports));
        Assert.Single(titles.All);
    }

    [Fact]
    public void ABookmarkOnADevice_IsShownOnlyWhileTheDeviceIsPluggedIn() {
        var none = Array.Empty<string>();
        var r8 = new[] { "Canon EOS R8" };

        Assert.False(BookmarkTitles.IsShown(Dcim, none));
        Assert.True(BookmarkTitles.IsShown(Dcim, r8));
        Assert.True(BookmarkTitles.IsShown(@"device:canon eos r8", r8));
        Assert.False(BookmarkTitles.IsShown(@"device:Pixel 6\Storage", r8));
        // Anything else is always shown - a folder whose disk is gone is greyed, not hidden.
        Assert.True(BookmarkTitles.IsShown(Reports, none));
        Assert.True(BookmarkTitles.IsShown("shell:RecycleBinFolder", none));
    }
}
