using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class ClipboardPasteTests {
    private static readonly string[] _files = { @"C:\src\a.txt" };


    [Fact]
    public void Choose_FilesFirst_TheRestLeft() {
        var choice = ClipboardPaste.Choose(new ClipboardFiles(_files, false, HasText: true, HasImage: true, HasAnything: true));

        Assert.Equal(PasteKind.Files, choice.Kind);
        Assert.Equal(new[] { PasteKind.Text, PasteKind.Image }, choice.Left);
    }

    /// <summary>Excel's cells: their text and a picture of them - the text is pasted (decision of 2026-09-24).</summary>
    [Fact]
    public void Choose_TextBeforeThePicture() {
        var choice = ClipboardPaste.Choose(new ClipboardFiles(Array.Empty<string>(), false, HasText: true, HasImage: true, HasAnything: true));

        Assert.Equal(PasteKind.Text, choice.Kind);
        Assert.Equal(new[] { PasteKind.Image }, choice.Left);
    }

    [Fact]
    public void Choose_ThePicture_WhenAlone() {
        var choice = ClipboardPaste.Choose(new ClipboardFiles(Array.Empty<string>(), false, HasImage: true, HasAnything: true));

        Assert.Equal(PasteKind.Image, choice.Kind);
        Assert.Empty(choice.Left);
    }

    /// <summary>An attachment copied in Outlook is files, not the text of its name beside them.</summary>
    [Fact]
    public void Choose_FilesNotOnDisk_PasteNothingInTheirPlace() {
        var choice = ClipboardPaste.Choose(new ClipboardFiles(Array.Empty<string>(), false, HasUnsupportedFiles: true, HasText: true, HasAnything: true));

        Assert.Equal(PasteKind.None, choice.Kind);
    }

    [Fact]
    public void Choose_OtherFormatsOnly_Nothing() {
        Assert.Equal(PasteKind.None, ClipboardPaste.Choose(new ClipboardFiles(Array.Empty<string>(), false, HasAnything: true)).Kind);
    }


    [Fact]
    public void Controller_NotesTextAndPicture_AndCanPasteThem() {
        var system = new FakeSystemClipboard {
            Content = new ClipboardFiles(Array.Empty<string>(), false, HasText: true, HasImage: true, HasAnything: true),
        };
        var clip = new ClipboardController(system);
        int changed = 0;
        clip.Changed += (_, _) => changed++;

        Assert.True(clip.SyncFromSystem());

        Assert.False(clip.HasContent);
        Assert.True(clip.HasText);
        Assert.True(clip.HasImage);
        Assert.True(clip.CanPaste);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Controller_OtherFormatsOnly_CanStillPaste_ToSayWhatWasThere() {
        var system = new FakeSystemClipboard { Content = new ClipboardFiles(Array.Empty<string>(), false, HasAnything: true) };
        var clip = new ClipboardController(system);

        clip.SyncFromSystem();

        Assert.True(clip.CanPaste);
    }

    [Fact]
    public void Controller_OurOwnCopy_ReplacesTheText() {
        var system = new FakeSystemClipboard {
            Content = new ClipboardFiles(Array.Empty<string>(), false, HasText: true, HasAnything: true),
        };
        var clip = new ClipboardController(system);
        clip.SyncFromSystem();

        clip.Copy(_files);

        Assert.False(clip.HasText);
        Assert.False(clip.SyncFromSystem());
    }

    [Fact]
    public void Controller_Clear_ForgetsTheTextToo() {
        var system = new FakeSystemClipboard {
            Content = new ClipboardFiles(Array.Empty<string>(), false, HasText: true, HasAnything: true),
        };
        var clip = new ClipboardController(system);
        clip.SyncFromSystem();

        clip.Clear();

        Assert.False(clip.CanPaste);
    }
}
