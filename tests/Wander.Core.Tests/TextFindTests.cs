using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class TextFindTests {
    [Fact]
    public void All_IgnoresCase_NoOverlap() {
        Assert.Equal(new[] { 0, 8, 17 }, TextFind.All("Бюджет, бюджет и БЮДЖЕТ", "бюджет"));
        Assert.Equal(new[] { 0, 2 }, TextFind.All("aaaa", "aa"));
    }

    [Fact]
    public void All_EmptyQueryOrText_Nothing() {
        Assert.Empty(TextFind.All("text", ""));
        Assert.Empty(TextFind.All("", "x"));
    }

    [Fact]
    public void All_StopsAtTheCap() {
        Assert.Equal(TextFind.MaxMatches, TextFind.All(new string('a', TextFind.MaxMatches + 50), "a").Count);
    }

    [Fact]
    public void FirstFrom_AtOrAfterTheCaret_ElseTheFirst() {
        var matches = new[] { 5, 20, 40 };

        Assert.Equal(0, TextFind.FirstFrom(matches, 0));
        Assert.Equal(1, TextFind.FirstFrom(matches, 20));
        Assert.Equal(2, TextFind.FirstFrom(matches, 21));
        Assert.Equal(0, TextFind.FirstFrom(matches, 41));
        Assert.Equal(-1, TextFind.FirstFrom(Array.Empty<int>(), 0));
    }

    [Fact]
    public void Step_WrapsBothWays() {
        Assert.Equal(1, TextFind.Step(0, 3, backwards: false));
        Assert.Equal(0, TextFind.Step(2, 3, backwards: false));
        Assert.Equal(2, TextFind.Step(0, 3, backwards: true));
        Assert.Equal(0, TextFind.Step(-1, 3, backwards: false));
        Assert.Equal(2, TextFind.Step(-1, 3, backwards: true));
        Assert.Equal(-1, TextFind.Step(0, 0, backwards: false));
    }
}
