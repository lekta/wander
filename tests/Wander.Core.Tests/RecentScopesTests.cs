using Wander.Core.Shell;

namespace Wander.Core.Tests;

/// <summary>
/// The short memory behind the "Добавить" picker: what file types the user
/// was just right-clicking on.
/// </summary>
public class RecentScopesTests {

    [Fact]
    public void MostRecentGoesFirst() {
        var list = RecentScopes.Add(RecentScopes.Add(Empty, ".txt"), ".psd");

        Assert.Equal(new[] { ".psd", ".txt" }, list);
    }

    [Fact]
    public void RevisitingATypeMovesItUp_WithoutDuplicating() {
        var list = Add(".a", ".b", ".c", ".a");

        Assert.Equal(new[] { ".a", ".c", ".b" }, list);
    }

    [Fact]
    public void ListIsCapped() {
        var list = Add(".1", ".2", ".3", ".4", ".5", ".6", ".7");

        Assert.Equal(RecentScopes.Max, list.Count);
        Assert.Equal(".7", list[0]);
        Assert.DoesNotContain(".1", list);
    }

    [Fact]
    public void SameTypeAgain_ReturnsTheSameInstance() {
        // The caller persists on a difference, and right-clicking twice in
        // the same folder must not rewrite state.json.
        var first = Add(".txt", ".psd");
        var again = RecentScopes.Add(first, ".psd");

        Assert.Same(first, again);
    }

    [Fact]
    public void NothingToRemember_ChangesNothing() {
        var list = Add(".txt");

        Assert.Same(list, RecentScopes.Add(list, null));
        Assert.Same(list, RecentScopes.Add(list, ""));
    }

    [Theory]
    [InlineData(@"C:\work\photo.PSD", ".psd")]
    [InlineData(@"C:\work\archive.tar.gz", ".gz")]
    [InlineData(@"C:\work\README", null)]
    [InlineData(@"C:\work\folder", null)]
    // A dot in a parent folder is not the extension of a file that has none.
    [InlineData(@"C:\v1.2\README", null)]
    [InlineData(@"C:\work\trailing.", null)]
    [InlineData(@".gitignore", null)]
    [InlineData(null, null)]
    public void ExtensionOf_ReadsTheTypeOffAPath(string? path, string? expected) {
        Assert.Equal(expected, ShellScopes.ExtensionOf(path));
    }

    [Theory]
    [InlineData(@"C:\work\note.TXT", false, ".txt")]
    [InlineData(@"C:\work\README", false, null)]
    [InlineData(@"C:\work\sub", true, ShellScopes.Directory)]
    // A dot in a folder's name is not a type.
    [InlineData(@"C:\work\v1.2", true, ShellScopes.Directory)]
    public void MenuScopeOf_IsTheItemsOwnType(string path, bool isFolder, string? expected) {
        Assert.Equal(expected, ShellScopes.MenuScopeOf(path, isFolder, _ => throw new InvalidOperationException("not a shortcut")));
    }

    [Fact]
    public void MenuScopeOf_AShortcutStandsForItsTarget() {
        // The shell builds a link's menu from its target's handlers (stand
        // 2026-09-25): the rows belong to the target's type, not to .lnk.
        Assert.Equal(".psd", ShellScopes.MenuScopeOf(@"C:\work\art.lnk", false, _ => @"D:\art\cover.PSD"));
        Assert.Equal(ShellScopes.Directory, ShellScopes.MenuScopeOf(@"C:\work\art.lnk", true, _ => @"D:\art"));
        // Nothing to name: an unreadable link, one to a file with no
        // extension, one to another link.
        Assert.Null(ShellScopes.MenuScopeOf(@"C:\work\broken.lnk", false, _ => null));
        Assert.Null(ShellScopes.MenuScopeOf(@"C:\work\readme.lnk", false, _ => @"D:\README"));
        Assert.Null(ShellScopes.MenuScopeOf(@"C:\work\chain.lnk", false, _ => @"D:\other.lnk"));
    }


    private static IReadOnlyList<string> Empty => Array.Empty<string>();

    private static IReadOnlyList<string> Add(params string[] scopes) {
        var list = Empty;
        foreach (string scope in scopes) {
            list = RecentScopes.Add(list, scope);
        }

        return list;
    }
}
