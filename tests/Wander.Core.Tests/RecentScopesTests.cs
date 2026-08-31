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


    private static IReadOnlyList<string> Empty => Array.Empty<string>();

    private static IReadOnlyList<string> Add(params string[] scopes) {
        var list = Empty;
        foreach (string scope in scopes) {
            list = RecentScopes.Add(list, scope);
        }

        return list;
    }
}
