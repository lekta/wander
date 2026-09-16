using Wander.Core.Actions;
using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class FileTypeGroupsTests {
    [Theory]
    [InlineData(FileTypeGroup.Images, "a.jpg", true)]
    [InlineData(FileTypeGroup.Images, "a.cr2", true)]
    [InlineData(FileTypeGroup.Video, "a.mkv", true)]
    [InlineData(FileTypeGroup.Audio, "a.flac", true)]
    [InlineData(FileTypeGroup.TextAndCode, "a.cs", true)]
    [InlineData(FileTypeGroup.TextAndCode, "a.md", true)]
    [InlineData(FileTypeGroup.TextAndCode, "a.txt", true)]
    [InlineData(FileTypeGroup.Documents, "a.docx", true)]
    [InlineData(FileTypeGroup.Archives, "a.7z", true)]
    [InlineData(FileTypeGroup.Images, "a.mp4", false)]
    [InlineData(FileTypeGroup.Video, "a.jpg", false)]
    public void Matches_ByExtension_CaseInsensitively(FileTypeGroup group, string name, bool expected) {
        Assert.Equal(expected, FileTypeGroups.Matches(group, File(name)));
        Assert.Equal(expected, FileTypeGroups.Matches(group, File(name.ToUpperInvariant())));
    }

    [Fact]
    public void All_TakesEverything_Folders_OnlyFolders() {
        Assert.True(FileTypeGroups.Matches(FileTypeGroup.All, File("a.xyz")));
        Assert.True(FileTypeGroups.Matches(FileTypeGroup.All, Dir("d")));
        Assert.True(FileTypeGroups.Matches(FileTypeGroup.Folders, Dir("d")));
        Assert.False(FileTypeGroups.Matches(FileTypeGroup.Folders, File("a.jpg")));
        // A folder is never a picture, whatever it is called.
        Assert.False(FileTypeGroups.Matches(FileTypeGroup.Images, Dir("holiday.jpg")));
    }

    [Fact]
    public void Classify_NamesTheOneGroupEverythingShares() {
        Assert.Null(FileTypeGroups.Classify(Array.Empty<FileSystemEntry>()));
        Assert.Equal(FileTypeGroup.Images, FileTypeGroups.Classify(new[] { File("a.jpg"), File("b.png") }));
        Assert.Equal(FileTypeGroup.Folders, FileTypeGroups.Classify(new[] { Dir("a"), Dir("b") }));
        Assert.Null(FileTypeGroups.Classify(new[] { File("a.jpg"), File("b.mp4") }));
        Assert.Null(FileTypeGroups.Classify(new[] { File("a.jpg"), Dir("b") }));
    }

    [Fact]
    public void Selector_Mask_WinsOverGroup_AndAcceptsThreeSpellings() {
        var selector = new FileTypeSelector(FileTypeGroup.Video, "*.psd; ai ;.indd");

        Assert.True(selector.Matches(File("a.PSD")));
        Assert.True(selector.Matches(File("a.ai")));
        Assert.True(selector.Matches(File("a.indd")));
        Assert.False(selector.Matches(File("a.mp4")));
        Assert.False(selector.Matches(Dir("a.psd")));
    }

    [Fact]
    public void Selector_Star_MeansAnyFile() {
        Assert.True(new FileTypeSelector(Mask: "*").Matches(File("a.xyz")));
        Assert.True(new FileTypeSelector(Mask: "*.*").Matches(File("a.xyz")));
        Assert.False(new FileTypeSelector(Mask: "*").Matches(Dir("d")));
    }

    [Fact]
    public void EveryGroupHasANameKey() {
        foreach (var group in Enum.GetValues<FileTypeGroup>()) {
            Assert.StartsWith("FileType", FileTypeGroups.NameKey(group));
        }
    }


    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, @"C:\x\" + name, EntryKind.File, 1, DateTime.UnixEpoch, false, false, false, false);
    }

    private static FileSystemEntry Dir(string name) {
        return new FileSystemEntry(name, @"C:\x\" + name, EntryKind.Directory, null, DateTime.UnixEpoch, false, false, false, false);
    }
}
