using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class PathRewriteTests {
    private const string Old = @"C:\photos";
    private const string New = @"D:\archive\photos";


    [Fact]
    public void TheFolderItself_MovesToTheNewRoot() {
        Assert.Equal(New, PathRewrite.Under(Old, Old, New));
    }

    [Fact]
    public void ADescendant_KeepsItsTailUnderTheNewRoot() {
        Assert.Equal(@"D:\archive\photos\2024\trip", PathRewrite.Under(@"C:\photos\2024\trip", Old, New));
    }

    [Fact]
    public void TrailingSeparatorsAndCase_DoNotMatter() {
        Assert.Equal(New, PathRewrite.Under(@"c:\PHOTOS\", Old + @"\", New + @"\"));
        Assert.Equal(@"D:\archive\photos\2024", PathRewrite.Under(@"C:\photos\2024/", Old, New));
    }

    [Fact]
    public void ASiblingWithTheSamePrefix_IsNotInside() {
        Assert.Null(PathRewrite.Under(@"C:\photos-old\a", Old, New));
        Assert.Null(PathRewrite.Under(@"C:\photosX", Old, New));
    }

    [Fact]
    public void Unrelated_Empty_AndNull_AnswerNull() {
        Assert.Null(PathRewrite.Under(@"C:\video\a.mp4", Old, New));
        Assert.Null(PathRewrite.Under(null, Old, New));
        Assert.Null(PathRewrite.Under("", Old, New));
        Assert.Null(PathRewrite.Under(Old, "", New));
    }
}
