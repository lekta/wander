using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class EntryVisibilityTests {
    private static FileSystemEntry Entry(string path, bool hidden = false, bool system = false, bool file = false) {
        return new FileSystemEntry(
            Name: Path.GetFileName(path.TrimEnd('\\')),
            FullPath: path,
            Kind: file ? EntryKind.File : EntryKind.Directory,
            Size: null,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: hidden,
            IsReadOnly: false,
            IsSystem: system,
            LinksToDirectory: false);
    }


    [Fact]
    public void Allows_HidesHiddenAndProtected_ByDefault() {
        var visibility = new EntryVisibility(ShowHidden: false, ShowSystem: false, HideSystemRootFolders: true);

        Assert.True(visibility.Allows(Entry(@"C:\photos")));
        Assert.False(visibility.Allows(Entry(@"C:\photos", hidden: true)));
        Assert.False(visibility.Allows(Entry(@"C:\Windows\System32\config", hidden: true, system: true)));
    }


    /// <summary>
    /// The <c>System</c> attribute on its own says nothing: Windows sets it
    /// on every folder that carries a <c>desktop.ini</c>, which is every
    /// folder whose icon was ever customised. Explorer shows those as the
    /// ordinary folders they are, and so must we — hiding somebody's
    /// «Изобразительное искусство» because it once got a custom icon is the
    /// bug this rule exists to prevent.
    /// </summary>
    [Fact]
    public void Allows_ShowsAFolderThatIsOnlySystem() {
        var visibility = new EntryVisibility(ShowHidden: false, ShowSystem: false, HideSystemRootFolders: true);

        Assert.True(visibility.Allows(Entry(@"C:\books\Йога", system: true)));
    }


    /// <summary>
    /// The pair is what Explorer's "hide protected operating system files"
    /// covers, and it stays hidden even with hidden files switched on —
    /// they are two separate checkboxes there for exactly this reason.
    /// </summary>
    [Fact]
    public void Allows_KeepsProtectedHidden_EvenWithHiddenFilesOn() {
        var visibility = new EntryVisibility(ShowHidden: true, ShowSystem: false, HideSystemRootFolders: false);

        Assert.False(visibility.Allows(Entry(@"C:\pagefile.sys", hidden: true, system: true)));
        Assert.True(visibility.Allows(Entry(@"C:\ProgramData", hidden: true)));
        Assert.True(visibility.Allows(Entry(@"C:\books\Йога", system: true)));
    }


    [Fact]
    public void Allows_LetsHiddenAndSystemThrough_WhenBothAreOn() {
        var visibility = new EntryVisibility(ShowHidden: true, ShowSystem: true, HideSystemRootFolders: false);

        Assert.True(visibility.Allows(Entry(@"C:\photos", hidden: true, system: true)));
    }


    /// <summary>
    /// The reason the third switch exists: someone who turns hidden and
    /// system files on wants to see their own files, not <c>$RECYCLE.BIN</c>
    /// in the root of every drive.
    /// </summary>
    [Fact]
    public void Allows_StillHidesVolumeRootPlumbing_WithHiddenAndSystemOn() {
        var visibility = new EntryVisibility(ShowHidden: true, ShowSystem: true, HideSystemRootFolders: true);

        Assert.False(visibility.Allows(Entry(@"C:\$RECYCLE.BIN", hidden: true, system: true)));
        Assert.False(visibility.Allows(Entry(@"D:\System Volume Information", hidden: true, system: true)));
        Assert.True(visibility.Allows(Entry(@"C:\Users", hidden: true)));
    }


    [Fact]
    public void Allows_ShowsVolumeRootPlumbing_WhenTheSwitchIsOff() {
        var visibility = new EntryVisibility(ShowHidden: true, ShowSystem: true, HideSystemRootFolders: false);

        Assert.True(visibility.Allows(Entry(@"C:\$RECYCLE.BIN", hidden: true, system: true)));
    }


    /// <summary>
    /// The same switch covers the bookkeeping Windows leaves in any folder:
    /// someone who shows hidden and system files wants their own files, not
    /// a desktop.ini next to every customised folder's contents.
    /// </summary>
    [Fact]
    public void Allows_HidesServiceFilesAnywhere_WithHiddenAndSystemOn() {
        var visibility = new EntryVisibility(ShowHidden: true, ShowSystem: true, HideSystemRootFolders: true);

        Assert.False(visibility.Allows(Entry(@"C:\photos\desktop.ini", hidden: true, system: true, file: true)));
        Assert.False(visibility.Allows(Entry(@"\\server\share\photos\Thumbs.db", hidden: true, file: true)));
        Assert.False(visibility.Allows(Entry(@"E:\from mac\.DS_Store", file: true)));
        Assert.True(visibility.Allows(Entry(@"C:\photos\settings.ini", hidden: true, file: true)));
        Assert.True(visibility.Allows(Entry(@"C:\photos\desktop.ini")));
    }


    /// <summary>
    /// The attributes are not what makes a desktop.ini bookkeeping: one an
    /// installer wrote carries none, and it is not shown with hidden files
    /// off either.
    /// </summary>
    [Fact]
    public void Allows_HidesPlainServiceFiles_WithoutHiddenFiles() {
        var visibility = new EntryVisibility(ShowHidden: false, ShowSystem: false, HideSystemRootFolders: false);

        Assert.False(visibility.Allows(Entry(@"C:\photos\desktop.ini", file: true)));
        Assert.True(visibility.Allows(Entry(@"C:\photos\IMG_0001.jpg", file: true)));
    }


    /// <summary>
    /// What a Mac leaves on a drive or in a zip: "._name" beside each file,
    /// the zip's __MACOSX folder of them. A file by that name stays a file,
    /// a folder whose name only starts with "._" stays a folder.
    /// </summary>
    [Fact]
    public void Allows_HidesTheMacsLeftovers() {
        var visibility = new EntryVisibility(ShowHidden: true, ShowSystem: true, HideSystemRootFolders: true);

        Assert.False(visibility.Allows(Entry(@"E:\from mac\._IMG_0001.jpg", file: true)));
        Assert.False(visibility.Allows(Entry(@"C:\photos.zip\__MACOSX")));
        Assert.True(visibility.Allows(Entry(@"E:\from mac\IMG_0001.jpg", file: true)));
        Assert.True(visibility.Allows(Entry(@"E:\from mac\._", file: true)));
        Assert.True(visibility.Allows(Entry(@"E:\from mac\__MACOSX", file: true)));
        Assert.True(visibility.Allows(Entry(@"E:\from mac\._drafts")));
    }


    [Fact]
    public void Allows_ShowsServiceFiles_WhenTheSwitchIsOff() {
        var visibility = new EntryVisibility(ShowHidden: true, ShowSystem: true, HideSystemRootFolders: false);

        Assert.True(visibility.Allows(Entry(@"C:\photos\desktop.ini", hidden: true, system: true, file: true)));
        Assert.True(visibility.Allows(Entry(@"E:\from mac\.DS_Store", file: true)));
    }


    [Fact]
    public void All_ShowsEverything() {
        Assert.True(EntryVisibility.All.Allows(Entry(@"C:\$RECYCLE.BIN", hidden: true, system: true)));
    }
}
