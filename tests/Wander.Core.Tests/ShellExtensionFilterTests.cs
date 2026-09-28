using Wander.Core.Shell;

namespace Wander.Core.Tests;

/// <summary>
/// The filter over the context-menu table in settings: what a row shows is
/// what it can be found by, and a typed extension finds the menu of that
/// type rather than the rows that happen to name it.
/// </summary>
public class ShellExtensionFilterTests {
    private static readonly ShellExtensionRow _sevenZip = Row("7-Zip", app: "7-Zip", ShellScopes.AllFiles, ShellScopes.Directory);
    private static readonly ShellExtensionRow _vlc = Row("Воспроизвести в VLC", app: "VLC media player", ShellScopes.Directory);
    private static readonly ShellExtensionRow _mp4 = Row("Открыть в MPC", app: "MPC-HC", ".mp4");
    private static readonly ShellExtensionRow _everything = Row("Проверить", app: "Antivirus", ShellScopes.AllFilesystemObjects);
    private static readonly ShellExtensionRow[] _table = { _sevenZip, _vlc, _mp4, _everything };


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoText_LetsEveryRowThrough(string? query) {
        Assert.Equal(_table, Filter(query));
    }

    [Fact]
    public void CaptionAndApplication_AreFoundIgnoringCase() {
        Assert.Equal(new[] { _vlc }, Filter("vlc"));
        Assert.Equal(new[] { _mp4 }, Filter("mpc-hc"));
        Assert.Equal(new[] { _vlc }, Filter("  воспроизвести "));
    }

    [Fact]
    public void AFileType_IsFoundByWhatTheTableCallsIt() {
        // "папки" is on the screen, "Directory" is not; "файлы и папки" is
        // about folders too.
        Assert.Equal(new[] { _sevenZip, _vlc, _everything }, Filter("папк"));
        Assert.Empty(Filter("Directory"));
    }

    [Fact]
    public void AnExtension_FindsItsOwnRowsAndThoseEveryFileGets() {
        // Everything shown in the menu of an .mp4: 7-Zip and the antivirus
        // are there as much as the player is.
        Assert.Equal(new[] { _sevenZip, _mp4, _everything }, Filter(".mp4"));
        Assert.Equal(new[] { _sevenZip, _mp4, _everything }, Filter(".MP4"));
    }

    [Fact]
    public void WithoutTheDot_AnExtensionIsJustText() {
        Assert.Equal(new[] { _mp4 }, Filter("mp4"));
    }

    [Fact]
    public void ATypeNoRowHas_StillFindsTheRowsForEveryFile() {
        // The table does not list the type, the menu of such a file still
        // has these rows.
        Assert.Equal(new[] { _sevenZip, _everything }, Filter(".psd"));
    }

    [Fact]
    public void ALoneDot_IsNotAnExtension() {
        // Text like any other: the rows that show a dot, not every file's.
        Assert.Equal(new[] { _mp4 }, Filter("."));
    }


    private static IReadOnlyList<ShellExtensionRow> Filter(string? query) {
        return _table.Where(row => ShellExtensionFilter.Matches(row, query, Title)).ToArray();
    }

    /// <summary>The scope names the table shows, for the ones a test types.</summary>
    private static string Title(string scope) {
        return scope switch {
            ShellScopes.AllFiles => "все файлы",
            ShellScopes.AllFilesystemObjects => "файлы и папки",
            ShellScopes.Directory => "папки",
            _ => scope,
        };
    }

    private static ShellExtensionRow Row(string title, string app, params string[] scopes) {
        return new ShellExtensionRow { Key = title, Title = title, AppName = app, Scopes = scopes };
    }
}
