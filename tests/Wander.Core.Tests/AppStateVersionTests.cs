using System.Text.Json;
using Wander.Core.Persistence;

namespace Wander.Core.Tests;

/// <summary>
/// The shape number of <c>state.json</c> (PLAN AD11): what an older build
/// compares against before writing over a file a newer one wrote.
/// </summary>
public class AppStateVersionTests {

    [Fact]
    public void AFreshState_CarriesTheCurrentShape() {
        Assert.Equal(AppState.CurrentVersion, new AppState().Version);
    }

    [Fact]
    public void AFileWrittenBeforeTheFieldExisted_ReadsAsTheCurrentShape() {
        // The whole point of the default: every state.json on disk today
        // has no Version, and none of them is "shape 0, refuse to touch".
        var state = JsonSerializer.Deserialize<AppState>("""{"Favorites":["C:\\work"]}""");

        Assert.NotNull(state);
        Assert.Equal(AppState.CurrentVersion, state.Version);
        Assert.Equal(new[] { @"C:\work" }, state.Favorites);
    }

    [Fact]
    public void ANewerShape_SurvivesTheRoundTrip_SoTheGuardCanSeeIt() {
        string json = JsonSerializer.Serialize(new AppState { Version = AppState.CurrentVersion + 1 });

        Assert.Equal(AppState.CurrentVersion + 1, JsonSerializer.Deserialize<AppState>(json)!.Version);
    }
}
