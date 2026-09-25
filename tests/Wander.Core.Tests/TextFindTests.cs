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
    public void Count_AgreesWithAll_AcrossBlockBoundaries() {
        // Longer than one block of the reader (64K characters), under the
        // cap, and with a match cut in two by the block's edge: the 3856th
        // repeat starts at 65 535.
        string text = string.Concat(Enumerable.Repeat("бюджет и БЮДЖЕТ, ", 4_000));

        Assert.Equal(8_000, TextFind.All(text, "бюджет").Count);
        Assert.Equal(8_000, TextFind.Count(new StringReader(text), "бюджет"));
        Assert.Equal(2, TextFind.Count(new StringReader("aaaa"), "aa"));
    }

    [Fact]
    public void Count_OnlyPastTheSkippedStart() {
        Assert.Equal(1, TextFind.Count(new StringReader("find me, find me"), "find", skip: 3));
        Assert.Equal(0, TextFind.Count(new StringReader("find"), "find", skip: 10));
    }

    [Fact]
    public void Count_EmptyQuery_Nothing_AndStopsAtTheCap() {
        Assert.Equal(0, TextFind.Count(new StringReader("text"), ""));
        Assert.Equal(TextFind.MaxMatches, TextFind.Count(new StringReader(new string('a', TextFind.MaxMatches + 50)), "a"));
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
