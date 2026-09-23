using Wander.Core.FileSystem;
using Wander.Core.Panels;
using Wander.Core.Workspace;

namespace Wander.Core.Tests;

/// <summary>What the preview panes follow for a target (REDESIGN 4.5).</summary>
public class PreviewSubjectTests {
    private const string Folder = @"C:\work";


    [Fact]
    public void OneRow_IsShownAndDescribed() {
        var a = File("a.jpg");

        var subject = PreviewSubject.Of(Target.OfRows(new[] { a }, a), caretPath: null, new[] { a });

        Assert.Equal(PreviewSubjectKind.Rows, subject.Kind);
        Assert.Same(a, subject.Primary);
        Assert.Equal(new[] { a }, subject.Selection);
        Assert.Null(subject.Pair);
    }

    /// <summary>Two pictures split the pane, the upper one of the listing first, whichever was clicked last.</summary>
    [Fact]
    public void TwoPictures_AreAPair_InListingOrder() {
        var a = File("a.jpg");
        var b = File("b.jpg");

        var subject = PreviewSubject.Of(Target.OfRows(new[] { b, a }, b), caretPath: b.FullPath, new[] { a, b });

        Assert.Equal((a, b), subject.Pair);
        Assert.Same(a, subject.Primary);
    }

    /// <summary>In a selection of several, the row with the focus rectangle is the one shown.</summary>
    [Fact]
    public void SeveralRows_ShowTheOneWithTheCaret() {
        var a = File("a.jpg");
        var b = File("b.txt");
        var c = File("c.txt");

        var subject = PreviewSubject.Of(Target.OfRows(new[] { a, b, c }, a), caretPath: c.FullPath, new[] { a, b, c });

        Assert.Same(c, subject.Primary);
        Assert.Equal(3, subject.Selection.Count);
    }

    [Fact]
    public void SeveralRows_WithTheCaretElsewhere_ShowTheCurrentRow() {
        var a = File("a.txt");
        var b = File("b.txt");
        var c = File("c.txt");

        var subject = PreviewSubject.Of(Target.OfRows(new[] { a, b, c }, b), caretPath: File("d.txt").FullPath, new[] { a, b, c });

        Assert.Same(b, subject.Primary);
    }

    /// <summary>A panel row: the folder, to be read and shown alone.</summary>
    [Fact]
    public void PanelRow_IsItsFolder() {
        var subject = PreviewSubject.Of(Target.OfPanelRow(Pane.Drives, @"D:\photos"), caretPath: null, Array.Empty<FileSystemEntry>());

        Assert.Equal(PreviewSubjectKind.Folder, subject.Kind);
        Assert.Equal(@"D:\photos", subject.Folder);
        Assert.Empty(subject.Selection);
    }

    /// <summary>No target: the pane describes the open folder.</summary>
    [Fact]
    public void NoTarget_IsTheOpenFolder() {
        Assert.Same(PreviewSubject.OpenFolder, PreviewSubject.Of(Target.None, caretPath: null, Array.Empty<FileSystemEntry>()));
        Assert.Same(PreviewSubject.OpenFolder, PreviewSubject.Of(Target.OfBackground(Folder), caretPath: null, Array.Empty<FileSystemEntry>()));
    }


    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(
            name, Path.Combine(Folder, name), EntryKind.File, 10, DateTime.UnixEpoch, false, false, false, false);
    }
}
