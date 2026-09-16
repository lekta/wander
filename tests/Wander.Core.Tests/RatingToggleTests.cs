using Wander.Core.Companions;

namespace Wander.Core.Tests;

public class RatingToggleTests {
    [Fact]
    public void AlreadySetOnEveryTarget_Clears() {
        Assert.Equal(0, RatingToggle.Resolve(3, new int?[] { 3, 3, 3 }));
    }

    [Fact]
    public void SetOnSomeTargetsOnly_SetsItOnAll() {
        Assert.Equal(3, RatingToggle.Resolve(3, new int?[] { 3, null, 2 }));
    }

    [Fact]
    public void NothingRecordedAnywhere_Sets() {
        Assert.Equal(4, RatingToggle.Resolve(4, new int?[] { null, null }));
    }

    /// <summary>One file: the click that always was - set, or take it back.</summary>
    [Fact]
    public void OneTarget_TogglesTheClickedValue() {
        Assert.Equal(0, RatingToggle.Resolve(2, new int?[] { 2 }));
        Assert.Equal(2, RatingToggle.Resolve(2, new int?[] { 1 }));
        Assert.Equal(2, RatingToggle.Resolve(2, new int?[] { null }));
    }

    [Fact]
    public void NoTargets_Sets() {
        Assert.Equal(5, RatingToggle.Resolve(5, Array.Empty<int?>()));
    }

    [Fact]
    public void ZeroClicked_Clears() {
        Assert.Equal(0, RatingToggle.Resolve(0, new int?[] { 3 }));
    }
}
