using Wander.Core.Appearance;

namespace Wander.Core.Tests;

public class UiThemesTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void System_FollowsTheAppModeOfWindows(bool windowsDark) {
        Assert.Equal(windowsDark, UiThemes.IsDark(UiTheme.System, windowsDark));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AChoice_WinsOverWindows(bool windowsDark) {
        Assert.False(UiThemes.IsDark(UiTheme.Light, windowsDark));
        Assert.True(UiThemes.IsDark(UiTheme.Dark, windowsDark));
    }
}
