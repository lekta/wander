using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class CountCaptionTests {
    private static readonly FakeTextSource _text = new(new Dictionary<string, string> {
        [CountCaption.ExtensionKey] = "{0} {1}",
        [CountCaption.RawKey] = "{0} RAW",
        ["MenuCaptionImages"] = "{0} изображение|{0} изображения|{0} изображений",
        ["MenuCaptionVideo"] = "{0} видео",
        [CountCaption.FilesKey] = "{0} файл|{0} файла|{0} файлов",
        [CountCaption.FoldersKey] = "{0} папка|{0} папки|{0} папок",
        [CountCaption.ItemsKey] = "{0} элемент|{0} элемента|{0} элементов",
    });


    [Fact]
    public void OneExtension_IsNamed_InUpperCase() {
        Assert.Equal("3 CR3", CountCaption.Of(Files("a.CR3", "b.cr3", "c.Cr3"), _text));
        // Extension before the group: all of them video, but one kind of it.
        Assert.Equal("5 MP4", CountCaption.Of(Files("a.mp4", "b.mp4", "c.mp4", "d.mp4", "e.mp4"), _text));
    }

    [Fact]
    public void RawOfSeveralMakers_IsRaw_AndRawWithOtherPictures_ArePictures() {
        Assert.Equal("2 RAW", CountCaption.Of(Files("a.cr3", "b.nef"), _text));
        Assert.Equal("2 изображения", CountCaption.Of(Files("a.cr3", "b.jpg"), _text));
        Assert.Equal("5 изображений", CountCaption.Of(Files("a.jpg", "b.png", "c.jpg", "d.jpg", "e.jpg"), _text));
    }

    [Fact]
    public void AnotherGroup_IsCountedByItsNoun() {
        Assert.Equal("2 видео", CountCaption.Of(Files("a.mp4", "b.mkv"), _text));
    }

    [Fact]
    public void FilesOfAnyKind_AreFiles_FoldersAreFolders_TogetherItems() {
        Assert.Equal("2 файла", CountCaption.Of(Files("a.jpg", "b.txt"), _text));
        Assert.Equal("2 файла", CountCaption.Of(Files("Makefile", "README"), _text));
        Assert.Equal("11 папок", CountCaption.Of(Enumerable.Range(0, 11).Select(i => Dir("d" + i)).ToArray(), _text));
        Assert.Equal("2 элемента", CountCaption.Of(new[] { Dir("a"), File("b.jpg") }, _text));
    }

    [Fact]
    public void Shape_NamesTheKey_AndTheExtension() {
        Assert.Equal((CountCaption.ExtensionKey, "JPG"), CountCaption.ShapeOf(Files("a.jpg", "b.JPG")));
        Assert.Equal((CountCaption.ItemsKey, (string?)null), CountCaption.ShapeOf(Array.Empty<FileSystemEntry>()));
    }


    private static FileSystemEntry[] Files(params string[] names) {
        return names.Select(File).ToArray();
    }

    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, @"C:\x\" + name, EntryKind.File, 1, DateTime.UnixEpoch, false, false, false, false);
    }

    private static FileSystemEntry Dir(string name) {
        return new FileSystemEntry(name, @"C:\x\" + name, EntryKind.Directory, null, DateTime.UnixEpoch, false, false, false, false);
    }
}
