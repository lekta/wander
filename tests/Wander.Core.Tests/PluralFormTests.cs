using Wander.Core.Localization;

namespace Wander.Core.Tests;

public class PluralFormTests {
    private const string Folders = "{0} папка|{0} папки|{0} папок";


    [Theory]
    [InlineData(1, "1 папка")]
    [InlineData(21, "21 папка")]
    [InlineData(101, "101 папка")]
    [InlineData(2, "2 папки")]
    [InlineData(4, "4 папки")]
    [InlineData(23, "23 папки")]
    [InlineData(0, "0 папок")]
    [InlineData(5, "5 папок")]
    [InlineData(11, "11 папок")]
    [InlineData(12, "12 папок")]
    [InlineData(14, "14 папок")]
    [InlineData(111, "111 папок")]
    [InlineData(112, "112 папок")]
    public void PicksTheRussianForm(long count, string expected) {
        Assert.Equal(expected, Text.PluralForm(Folders, count));
    }

    [Fact]
    public void AWordThatDoesNotChange_IsWrittenOnce() {
        Assert.Equal("1 видео", Text.PluralForm("{0} видео", 1));
        Assert.Equal("5 видео", Text.PluralForm("{0} видео", 5));
    }

    [Fact]
    public void AMissingKey_ComesBackAsItself() {
        // What tests see with no text source registered.
        Assert.Equal("MenuCaptionFolders", Text.Plural("MenuCaptionFolders", 3));
    }
}
