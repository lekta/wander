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


    [Fact]
    public void DriveRoot_IsProtected() {
        Assert.True(SystemPathGuard.IsProtected(@"C:\", out string reason));
        Assert.NotEmpty(reason);
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
    public void UserProfileRoot_IsProtected_ButDocumentsAreNot() {
        Assert.True(SystemPathGuard.IsProtected(_userProfile, out _));
        Assert.False(SystemPathGuard.IsProtected(Path.Combine(_userProfile, "Documents", "notes.txt"), out _));
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
}
