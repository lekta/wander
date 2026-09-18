using Wander.Core.Diagnostics;

namespace Wander.Core.Tests;

public class FileLockInfoTests {
    [Fact]
    public void Describe_NamesEveryHolderWithItsPid() {
        var lockers = new[] { new FileLockInfo(31424, "Windows PowerShell"), new FileLockInfo(812, "Word") };

        Assert.Equal("Windows PowerShell (PID 31424), Word (PID 812)", FileLockInfo.Describe(lockers));
        Assert.Equal("", FileLockInfo.Describe(Array.Empty<FileLockInfo>()));
    }
}
