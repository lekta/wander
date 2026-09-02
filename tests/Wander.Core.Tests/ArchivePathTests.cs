using Wander.Core.Shell;

namespace Wander.Core.Tests;

public class ArchivePathTests {
    private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase) {
        ".zip", ".7z", ".gz",
    };


    [Fact]
    public void Parse_ReturnsNull_ForOrdinaryPath() {
        Assert.Null(ArchivePath.Parse(@"D:\photos\raw\IMG.CR2", _extensions));
    }

    [Fact]
    public void Parse_ReturnsNull_ForEmptyAndForNoExtensions() {
        Assert.Null(ArchivePath.Parse("", _extensions));
        Assert.Null(ArchivePath.Parse(null, _extensions));
        Assert.Null(ArchivePath.Parse(@"D:\a\pack.zip", new HashSet<string>()));
    }

    [Fact]
    public void Parse_SplitsAtTheArchive() {
        var parsed = ArchivePath.Parse(@"D:\a\pack.zip\sub\b.txt", _extensions);

        Assert.NotNull(parsed);
        Assert.Equal(@"D:\a\pack.zip", parsed!.Archive);
        Assert.Equal(@"sub\b.txt", parsed.Inner);
        Assert.False(parsed.IsRoot);
        Assert.Equal("pack.zip", parsed.ArchiveName);
    }

    [Fact]
    public void Parse_ArchiveItself_IsRoot() {
        var parsed = ArchivePath.Parse(@"D:\a\pack.zip", _extensions);

        Assert.NotNull(parsed);
        Assert.Equal(@"D:\a\pack.zip", parsed!.Archive);
        Assert.Equal("", parsed.Inner);
        Assert.True(parsed.IsRoot);
    }

    [Fact]
    public void Parse_TrailingSeparator_IsStillRoot() {
        var parsed = ArchivePath.Parse(@"D:\a\pack.zip\", _extensions);

        Assert.NotNull(parsed);
        Assert.Equal(@"D:\a\pack.zip", parsed!.Archive);
        Assert.True(parsed.IsRoot);
    }

    [Fact]
    public void Parse_IgnoresCase() {
        var parsed = ArchivePath.Parse(@"D:\a\PACK.ZIP\B.TXT", _extensions);

        Assert.NotNull(parsed);
        Assert.Equal(@"D:\a\PACK.ZIP", parsed!.Archive);
        Assert.Equal("B.TXT", parsed.Inner);
    }

    [Fact]
    public void Parse_TarGz_SplitsOnTheLastExtension() {
        var parsed = ArchivePath.Parse(@"D:\a\logs.tar.gz\logs\today.log", _extensions);

        Assert.NotNull(parsed);
        Assert.Equal(@"D:\a\logs.tar.gz", parsed!.Archive);
        Assert.Equal(@"logs\today.log", parsed.Inner);
    }

    [Fact]
    public void Parse_ArchiveInTheMiddleOfThePath_TakesTheFirstOne() {
        // A folder called "x.zip" higher up wins over the archive below it:
        // the shell cannot look inside the second one without opening the
        // first, and the platform layer decides whether the first exists.
        var parsed = ArchivePath.Parse(@"D:\x.zip\inner\deep.7z\a.txt", _extensions);

        Assert.NotNull(parsed);
        Assert.Equal(@"D:\x.zip", parsed!.Archive);
        Assert.Equal(@"inner\deep.7z\a.txt", parsed.Inner);
    }

    [Fact]
    public void Parse_ForwardSlashes_AreSeparatorsToo() {
        var parsed = ArchivePath.Parse("D:/a/pack.zip/sub/b.txt", _extensions);

        Assert.NotNull(parsed);
        Assert.Equal("D:/a/pack.zip", parsed!.Archive);
        Assert.Equal("sub/b.txt", parsed.Inner);
    }

    [Fact]
    public void Parse_UncPath_KeepsItsRoot() {
        var parsed = ArchivePath.Parse(@"\\server\share\pack.zip\a.txt", _extensions);

        Assert.NotNull(parsed);
        Assert.Equal(@"\\server\share\pack.zip", parsed!.Archive);
        Assert.Equal("a.txt", parsed.Inner);
    }

    [Fact]
    public void Parse_ShellSentinel_IsNotAnArchive() {
        Assert.Null(ArchivePath.Parse(ShellPaths.RecycleBin, _extensions));
    }
}
