using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class SystemRootFoldersTests {
    [Theory]
    [InlineData(@"C:\$RECYCLE.BIN")]
    [InlineData(@"C:\$Recycle.Bin")]
    [InlineData(@"D:\System Volume Information")]
    [InlineData(@"C:\Recovery")]
    [InlineData(@"C:\$WinREAgent")]
    [InlineData(@"C:\Config.Msi")]
    [InlineData(@"E:\$Windows.~BT")]
    public void IsSystemRoot_RecognisesTheVolumeRootPlumbing(string path) {
        Assert.True(SystemRootFolders.IsSystemRoot(path));
    }


    [Fact]
    public void IsSystemRoot_IgnoresATrailingSeparator() {
        Assert.True(SystemRootFolders.IsSystemRoot(@"C:\$RECYCLE.BIN\"));
    }


    /// <summary>
    /// The whole point of anchoring to the volume root: a project folder
    /// that happens to be called "Recovery" is the user's own content and
    /// must stay in the listing.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\me\Recovery")]
    [InlineData(@"D:\backup\System Volume Information")]
    [InlineData(@"C:\Windows\$RECYCLE.BIN")]
    public void IsSystemRoot_OnlyMatchesDirectlyInAVolumeRoot(string path) {
        Assert.False(SystemRootFolders.IsSystemRoot(path));
    }


    [Theory]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\photos")]
    public void IsSystemRoot_LeavesOrdinaryRootFoldersAlone(string path) {
        Assert.False(SystemRootFolders.IsSystemRoot(path));
    }


    [Theory]
    [InlineData(@"C:\")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSystemRoot_IsFalseForARootOrNothingAtAll(string path) {
        Assert.False(SystemRootFolders.IsSystemRoot(path));
    }


    [Fact]
    public void IsSystemRoot_HandlesAUncShareRoot() {
        Assert.True(SystemRootFolders.IsSystemRoot(@"\\server\share\System Volume Information"));
        Assert.False(SystemRootFolders.IsSystemRoot(@"\\server\share\photos"));
    }
}
