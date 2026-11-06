using System.Text;
using Wander.Core.Folders;

namespace Wander.Core.Tests;

/// <summary>H1, decision B17: the folder type another program told Explorer, read as a hint.</summary>
public class DesktopIniTests {
    [Theory]
    [InlineData("Pictures")]
    [InlineData("Photos")]
    [InlineData("pictures")]
    public void PicturesOrPhotos_AreAHint(string type) {
        Assert.True(DesktopIni.SaysPictures($"[ViewState]\r\nMode=\r\nVid=\r\nFolderType={type}\r\n"));
    }

    [Theory]
    [InlineData("Documents")]
    [InlineData("Music")]
    [InlineData("Generic")]
    [InlineData("")]
    public void AnyOtherType_IsNot(string type) {
        Assert.False(DesktopIni.SaysPictures($"[ViewState]\r\nFolderType={type}\r\n"));
    }

    /// <summary>Only the view state's type: the same key elsewhere means something else, or nothing.</summary>
    [Fact]
    public void FolderTypeOutsideTheViewState_IsNotAHint() {
        Assert.False(DesktopIni.SaysPictures("[.ShellClassInfo]\r\nFolderType=Pictures\r\n[ViewState]\r\nMode=4\r\n"));
    }

    [Fact]
    public void Comments_SpacesAndOtherSections_AreSteppedOver() {
        string text = "; written by Explorer\r\n[.ShellClassInfo]\r\nIconResource=C:\\x.dll,1\r\n\r\n[ ViewState ]\r\n  FolderType = Pictures  \r\n";

        Assert.True(DesktopIni.SaysPictures(text));
    }

    /// <summary>Explorer writes the file in UTF-16 with its mark; other programs in ANSI or UTF-8.</summary>
    [Fact]
    public void TheFileIsReadWhateverItsEncoding() {
        const string text = "[ViewState]\r\nFolderType=Pictures\r\n";

        Assert.True(DesktopIni.SaysPictures(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()));
        Assert.True(DesktopIni.SaysPictures(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()));
        Assert.True(DesktopIni.SaysPictures(Encoding.ASCII.GetBytes(text)));
        Assert.False(DesktopIni.SaysPictures(Array.Empty<byte>()));
    }


    /// <summary>PLAN F1, decision 2026-11-06: a folder somebody gave an icon keeps the shell's picture.</summary>
    [Theory]
    [InlineData("[.ShellClassInfo]\r\nIconResource=C:\\Windows\\System32\\imageres.dll,-3\r\n")]
    [InlineData("[.ShellClassInfo]\r\nIconFile=folder.ico\r\nIconIndex=0\r\n")]
    [InlineData("; by Explorer\r\n[ .ShellClassInfo ]\r\n  iconresource = %SystemRoot%\\system32\\shell32.dll,4  \r\n")]
    public void AnIconOfItsOwn_IsSaid(string text) {
        Assert.True(DesktopIni.SetsIcon(text));
        Assert.True(DesktopIni.SetsIcon(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()));
    }

    /// <summary>A localised name alone, or the keys outside their section, give the folder no icon: it is drawn as any other.</summary>
    [Theory]
    [InlineData("[.ShellClassInfo]\r\nLocalizedResourceName=@%SystemRoot%\\system32\\shell32.dll,-21770\r\n")]
    [InlineData("[ViewState]\r\nIconResource=C:\\x.dll,1\r\n")]
    [InlineData("[.ShellClassInfo]\r\nIconResource=\r\n")]
    [InlineData("")]
    public void NoIconOfItsOwn_IsNotSaid(string text) {
        Assert.False(DesktopIni.SetsIcon(text));
    }
}
