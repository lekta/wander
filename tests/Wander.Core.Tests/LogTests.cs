using Wander.Core.Logging;

namespace Wander.Core.Tests;

/// <summary>
/// The static front of the log. Nothing here switches <see cref="Log.Details"/>
/// or <see cref="Log.RevealPaths"/> on: they are process-wide, and test classes
/// run side by side - the defaults are what is tested.
/// </summary>
public class LogTests {
    [Fact]
    public void ALineOfTheTrace_IsNotEvenPutTogether_WhileTheTraceIsOff() {
        int asked = 0;

        Log.Detail($"Selection: {Ask()} item(s)");

        Assert.Equal(0, asked);

        int Ask() {
            return ++asked;
        }
    }

    [Fact]
    public void APathOrAName_IsATokenByDefault() {
        Assert.Matches(@"^<C:\\~[0-9a-f]{6}\\~[0-9a-f]{6}\.txt>$", Log.Path(@"C:\Anna\notes.txt"));
        Assert.Matches(@"^<~[0-9a-f]{6}\.txt>$", Log.Path("notes.txt"));
        Assert.Equal("", Log.Path(null));
    }
}
