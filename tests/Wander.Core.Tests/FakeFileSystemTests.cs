using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class FakeFileSystemTests {
    [Fact]
    public void HasSubdirectories_ReturnsTrue_WhenChildDirectoryPresent() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\parent");
        fs.Directories.Add(@"C:\parent\child");

        Assert.True(fs.HasSubdirectories(@"C:\parent"));
    }

    [Fact]
    public void HasSubdirectories_ReturnsFalse_WhenOnlyFilesInside() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\leaf");
        fs.Files[@"C:\leaf\a.txt"] = new byte[0];

        Assert.False(fs.HasSubdirectories(@"C:\leaf"));
    }

    [Fact]
    public void HasSubdirectories_ReturnsFalse_ForUnknownPath() {
        var fs = new FakeFileSystem();

        Assert.False(fs.HasSubdirectories(@"C:\nowhere"));
    }
}
