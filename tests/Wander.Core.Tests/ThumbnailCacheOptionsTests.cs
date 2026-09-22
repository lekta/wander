using Wander.Core.Icons;

namespace Wander.Core.Tests;

public class ThumbnailCacheOptionsTests {
    [Theory]
    [InlineData(1.0, 256)]
    [InlineData(1.25, 256)]
    [InlineData(1.5, 384)]
    [InlineData(1.75, 384)]
    [InlineData(2.0, 512)]
    [InlineData(3.0, 512)]
    public void SideFollowsTheScale_InSteps(double scale, int side) {
        Assert.Equal(side, ThumbnailCacheOptions.SideFor(scale));
    }
}
