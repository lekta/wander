using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class FakeFileSystemTests {
    private const string Parent = @"C:\parent";
    private const string Child = @"C:\parent\child";
    private const string Leaf = @"C:\leaf";
    private const string LeafFile = @"C:\leaf\a.txt";
    private const string Nowhere = @"C:\nowhere";


    [Fact]
    public void HasSubdirectories_ReturnsTrue_WhenChildDirectoryPresent() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Parent);
        fs.Directories.Add(Child);

        Assert.True(fs.HasSubdirectories(Parent));
    }

    [Fact]
    public void HasSubdirectories_ReturnsFalse_WhenOnlyFilesInside() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Leaf);
        fs.Files[LeafFile] = new byte[0];

        Assert.False(fs.HasSubdirectories(Leaf));
    }

    [Fact]
    public void HasSubdirectories_ReturnsFalse_ForUnknownPath() {
        var fs = new FakeFileSystem();

        Assert.False(fs.HasSubdirectories(Nowhere));
    }
}
