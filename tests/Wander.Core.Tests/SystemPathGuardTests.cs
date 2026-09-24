using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

/// <summary>
/// SystemPathGuard is a pure function of the input path and the machine's
/// special-folder layout, so tests build inputs from the same environment
/// calls the guard itself uses.
/// </summary>
public class SystemPathGuardTests {
    private static readonly string _windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string _programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string _documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);


    [Fact]
    public void DriveRoot_IsProtected() {
        Assert.True(SystemPathGuard.IsProtected(@"C:\", out string reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void NetworkShareRoot_IsProtected_ButItsFoldersAreNot() {
        Assert.True(SystemPathGuard.IsProtected(@"\\server\share", out string reason));
        // The reason is the app's text; with none registered, its key.
        Assert.Equal("GuardShareRoot", reason);
        Assert.True(SystemPathGuard.IsProtected(@"\\server\share\", out _));
        Assert.False(SystemPathGuard.IsProtected(@"\\server\share\folder", out _));
    }

    [Fact]
    public void WindowsDirectory_IsProtected() {
        Assert.True(SystemPathGuard.IsProtected(_windowsDir, out _));
    }

    [Fact]
    public void WindowsDirectory_TrailingSlashAndCase_StillProtected() {
        Assert.True(SystemPathGuard.IsProtected(_windowsDir.ToUpperInvariant() + @"\", out _));
    }

    [Fact]
    public void PathInsideWindowsTree_IsProtected() {
        Assert.True(SystemPathGuard.IsProtected(Path.Combine(_windowsDir, "Temp", "some.tmp"), out string reason));
        Assert.Contains("Windows", reason);
    }

    [Fact]
    public void ProgramFilesRoot_IsProtected_ButContentsAreNot() {
        Assert.True(SystemPathGuard.IsProtected(_programFiles, out _));
        // Deleting an app's leftovers inside Program Files is a legitimate
        // (ACL-guarded) user action — only the root itself is blocked.
        Assert.False(SystemPathGuard.IsProtected(Path.Combine(_programFiles, "SomeApp", "old.dll"), out _));
    }

    [Fact]
    public void UserProfileRoot_IsProtected_ButWhatIsInItsFoldersIsNot() {
        Assert.True(SystemPathGuard.IsProtected(_userProfile, out _));
        Assert.False(SystemPathGuard.IsProtected(Path.Combine(_documents, "notes.txt"), out _));
    }

    /// <summary>
    /// The folders Windows keeps for the user are not ours to move, rename
    /// or delete (decided 2026-09-24); what is inside them is the user's.
    /// </summary>
    [Fact]
    public void UserFolders_AreProtected_ButTheirContentsAreNot() {
        Assert.True(SystemPathGuard.IsProtected(_documents, out string reason));
        Assert.Equal("GuardUserFolder", reason);
        Assert.True(SystemPathGuard.IsProtected(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), out _));
        Assert.True(SystemPathGuard.IsProtected(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) + @"\", out _));
        Assert.False(SystemPathGuard.IsProtected(Path.Combine(_documents, "Projects"), out _));
    }

    [Fact]
    public void AppData_AndTheThreeInIt_AreProtected_ButAnAppsFolderIsNot() {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Path.GetDirectoryName(local)!;

        Assert.True(SystemPathGuard.IsProtected(appData, out _));
        Assert.True(SystemPathGuard.IsProtected(local, out _));
        Assert.True(SystemPathGuard.IsProtected(Path.Combine(appData, "LocalLow"), out _));
        Assert.True(SystemPathGuard.IsProtected(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), out _));
        Assert.False(SystemPathGuard.IsProtected(Path.Combine(local, "SomeApp"), out _));
    }

    [Fact]
    public void UsersFolder_IsProtected() {
        string? usersDir = Path.GetDirectoryName(_userProfile);
        Assert.False(string.IsNullOrEmpty(usersDir));
        Assert.True(SystemPathGuard.IsProtected(usersDir!, out _));
    }

    [Fact]
    public void OrdinaryPaths_AreNotProtected() {
        Assert.False(SystemPathGuard.IsProtected(@"D:\work\project\file.cs", out _));
        Assert.False(SystemPathGuard.IsProtected(@"C:\temp", out _));
    }

    [Fact]
    public void EmptyOrGarbage_IsNotProtected() {
        Assert.False(SystemPathGuard.IsProtected("", out _));
        Assert.False(SystemPathGuard.IsProtected("   ", out _));
        Assert.False(SystemPathGuard.IsProtected("\0<>|", out _));
    }


    // --- Writing into a folder -------------------------------------------

    /// <summary>
    /// "Extract here" on a flash drive, an action's output beside a file in
    /// the profile: adding to a root is not taking it away.
    /// </summary>
    [Fact]
    public void DriveRoot_ProfileAndUserFolders_TakeWrites() {
        Assert.True(SystemPathGuard.MayWriteInto(@"E:\", out _));
        Assert.True(SystemPathGuard.MayWriteInto(@"C:\", out _));
        Assert.True(SystemPathGuard.MayWriteInto(@"\\server\share", out _));
        Assert.True(SystemPathGuard.MayWriteInto(_userProfile, out _));
        Assert.True(SystemPathGuard.MayWriteInto(_documents, out _));
    }

    [Fact]
    public void WindowsTree_TakesNoWrites_TheFolderItselfIncluded() {
        Assert.False(SystemPathGuard.MayWriteInto(_windowsDir, out string reason));
        Assert.Equal("GuardWindowsTree", reason);
        Assert.False(SystemPathGuard.MayWriteInto(Path.Combine(_windowsDir, "Temp"), out _));
        Assert.False(SystemPathGuard.MayWriteInto(_windowsDir.ToUpperInvariant() + @"\System32\", out _));
    }
}
