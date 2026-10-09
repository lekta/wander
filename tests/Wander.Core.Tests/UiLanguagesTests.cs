using Wander.Core.Localization;

namespace Wander.Core.Tests;

public class UiLanguagesTests {
    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("RU", "ru")]
    [InlineData("en", "en")]
    [InlineData("de", "en")]
    [InlineData("uk", "en")]
    [InlineData("", "en")]
    public void System_IsRussianOnlyOnARussianWindows(string windows, string expected) {
        Assert.Equal(expected, UiLanguages.Resolve(UiLanguage.System, windows));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("de")]
    public void AChoice_WinsOverWindows(string windows) {
        Assert.Equal("ru", UiLanguages.Resolve(UiLanguage.Russian, windows));
        Assert.Equal("en", UiLanguages.Resolve(UiLanguage.English, windows));
    }
}
