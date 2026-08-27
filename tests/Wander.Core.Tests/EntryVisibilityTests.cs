using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class EntryVisibilityTests {
    private static FileSystemEntry Entry(string path, bool hidden = false, bool system = false) {
        return new FileSystemEntry(
            Name: Path.GetFileName(path.TrimEnd('\\')),
            FullPath: path,
            Kind: EntryKind.Directory,
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


    [Fact]
    public void All_ShowsEverything() {
        Assert.True(EntryVisibility.All.Allows(Entry(@"C:\$RECYCLE.BIN", hidden: true, system: true)));
    }
}
