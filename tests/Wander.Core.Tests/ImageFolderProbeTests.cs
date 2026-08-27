using Wander.Core.Companions;
using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class ImageFolderProbeTests {

    private static FileSystemEntry Entry(string name, EntryKind kind = EntryKind.File) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\shoot\" + name,
            Kind: kind,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }

    private static bool Probe(params string[] names) {
        return ImageFolderProbe.IsImageFolder(
            names.Select(n => Entry(n)).ToList(), CompanionResolver.Default);
    }


    [Fact]
    public void EmptyFolder_IsNotAnImageFolder() {
        Assert.False(ImageFolderProbe.IsImageFolder(Array.Empty<FileSystemEntry>(), CompanionResolver.Default));
    }

    [Fact]
    public void MostlyImages_IsAnImageFolder() {
        Assert.True(Probe("a.jpg", "b.jpg", "c.jpg", "notes.txt"));
    }

    [Fact]
    public void ExactlyHalf_IsNot() {
        // The threshold is "more than half", not "at least half": a folder
        // split down the middle has no obvious right view.
        Assert.False(Probe("a.jpg", "notes.txt"));
    }

    [Fact]
    public void FourPhotographsAndNothingElse_IsAnImageFolder() {
        // No minimum count: a folder of four photographs is a folder of
        // photographs.
        Assert.True(Probe("a.cr3", "b.cr3", "c.cr3", "d.cr3"));
    }

    [Fact]
    public void RawCountsAsAnImage() {
        Assert.True(Probe("IMG_1.CR3", "IMG_2.NEF", "IMG_3.arw", "readme.md"));
    }

    [Fact]
    public void SidecarsDoNotCountAgainstThePhotographs() {
        // The case the whole denominator rule exists for: one .pp3 per RAW
        // drags a pure photo folder to exactly 50% and would switch the
        // gallery off in the folder that needs it most.
        Assert.True(Probe("IMG_1.CR3", "IMG_1.CR3.pp3", "IMG_2.CR3", "IMG_2.CR3.pp3"));
    }

    [Fact]
    public void XmpSidecarsDoNotCountEither() {
        Assert.True(Probe("IMG_1.CR3", "IMG_1.xmp", "IMG_2.CR3", "IMG_2.xmp"));
    }

    [Fact]
    public void BackupsAndTempFilesDoNotCount() {
        Assert.True(Probe("a.jpg", "b.jpg", "project.bak", "half.crdownload", "old.txt~"));
    }

    [Fact]
    public void FoldersAreIgnoredEntirely() {
        // Two photographs among four subfolders is still a folder of
        // photographs — subfolders are how a shoot gets organised.
        Assert.True(ImageFolderProbe.IsImageFolder(
            new[] {
                Entry("selects", EntryKind.Directory),
                Entry("exported", EntryKind.Directory),
                Entry("a.jpg"),
                Entry("b.jpg"),
            },
            CompanionResolver.Default));
    }

    [Fact]
    public void FoldersAlone_AreNotAnImageFolder() {
        Assert.False(ImageFolderProbe.IsImageFolder(
            new[] { Entry("a", EntryKind.Directory), Entry("b", EntryKind.Directory) },
            CompanionResolver.Default));
    }

    [Fact]
    public void CodeFolder_IsNot() {
        Assert.False(Probe("Program.cs", "App.xaml", "icon.png", "readme.md"));
    }
}
