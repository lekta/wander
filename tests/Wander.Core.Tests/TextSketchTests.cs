using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class TextSketchTests {
    [Fact]
    public void Words_AreBarsWithTheirSpacesBetween() {
        var bars = TextSketch.Lay("ab cd  efg\nnext");

        Assert.Equal(new[] { 0.0, 3.0, 7.0 }, bars.Take(3).Select(b => b.Left));
        Assert.Equal(new[] { 2.0, 2.0, 3.0 }, bars.Take(3).Select(b => b.Width));
        Assert.All(bars, b => Assert.Equal(0, b.Rank));
        Assert.Single(bars.Take(3).Select(b => b.Top).Distinct());
    }

    [Fact]
    public void BlankLines_LeaveRows_TwoAtMost() {
        var bars = TextSketch.Lay("one\ntwo\n\nthree\n\n\n\n\nfour");

        Assert.Equal(new[] { 0.0, 1, 3, 6 }, bars.Select(b => Math.Floor(b.Top)));
    }

    [Fact]
    public void Indent_IsKept_TabToItsStop() {
        var bars = TextSketch.Lay("a\n  b\n\tc\n  \td");

        Assert.Equal(new[] { 0.0, 2, 4, 4 }, bars.Select(b => b.Left));
    }

    [Fact]
    public void LongLine_WrapsAtAWord_InsideThePage() {
        string line = string.Join(' ', Enumerable.Repeat("word", 30));
        var bars = TextSketch.Lay("x\n" + line);
        var wrapped = bars.Skip(1).ToList();

        Assert.All(wrapped, b => Assert.True(b.Left + b.Width <= TextSketch.Columns));
        Assert.Equal(30, wrapped.Count);
        Assert.Equal(4, wrapped.Select(b => b.Top).Distinct().Count());
        Assert.All(wrapped.GroupBy(b => b.Top), row => Assert.Equal(0, row.Min(b => b.Left)));
    }

    [Fact]
    public void ListItem_WrapsUnderItsText() {
        string item = "- " + string.Join(' ', Enumerable.Repeat("item", 20));
        var bars = TextSketch.Lay("x\n" + item);
        var second = bars.Skip(1).GroupBy(b => b.Top).ElementAt(1);

        Assert.Equal(2, second.Min(b => b.Left));
    }

    [Fact]
    public void WordWiderThanThePage_GoesOnInPieces() {
        var bars = TextSketch.Lay("x\n" + new string('w', 100));
        var pieces = bars.Skip(1).ToList();

        Assert.Equal(100, pieces.Sum(b => b.Width), 6);
        Assert.Equal(3, pieces.Count);
        Assert.All(pieces, b => Assert.True(b.Left + b.Width <= TextSketch.Columns));
    }

    [Fact]
    public void Hashes_AreAHeading_WithoutTheHashes() {
        var bars = TextSketch.Lay("# Title\n### Small\n#hashtag\nbody");

        Assert.Equal(1, bars[0].Rank);
        Assert.Equal(0, bars[0].Left);
        Assert.True(bars[0].Width > "Title".Length);
        Assert.Equal(3, bars[1].Rank);
        Assert.Equal(0, bars[2].Rank);
        Assert.Equal("#hashtag".Length, bars[2].Width);
        Assert.True(bars[0].Height > bars[3].Height);
    }

    [Fact]
    public void Underline_MakesTheLineAboveAHeading_AndIsNotDrawn() {
        var bars = TextSketch.Lay("Title\n=====\nSub\n---\nbody");

        Assert.Equal(new[] { 1, 2, 0 }, bars.Select(b => b.Rank));
    }

    [Fact]
    public void Rule_AfterABlankLine_IsDrawn() {
        var bars = TextSketch.Lay("one\ntwo\n\n-----\nthree");

        Assert.Equal(4, bars.Count);
        Assert.All(bars, b => Assert.Equal(0, b.Rank));
    }

    [Fact]
    public void ShortFirstLine_BeforeABlank_IsTheTitle() {
        Assert.Equal(1, TextSketch.Lay("Shopping\n\nmilk bread")[0].Rank);
        Assert.Equal(1, TextSketch.Lay("\n\n  Shopping\n\nmilk")[0].Rank);
        Assert.Equal(0, TextSketch.Lay("Shopping\nmilk")[0].Rank);
        Assert.Equal(0, TextSketch.Lay("- milk\n\nbread")[0].Rank);
        Assert.Equal(0, TextSketch.Lay(new string('a', TextSketch.TitleLimit + 1) + "\n\nb")[0].Rank);
    }

    [Fact]
    public void Page_EndsAtItsLastRow() {
        string text = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"line {i}"));
        var bars = TextSketch.Lay(text);

        Assert.All(bars, b => Assert.True(b.Top + b.Height <= TextSketch.Rows));
        Assert.Equal(TextSketch.Rows, bars.Select(b => b.Top).Distinct().Count());
    }

    [Fact]
    public void CarriageReturns_AndEmptyText() {
        Assert.Equal(TextSketch.Lay("a b\nc"), TextSketch.Lay("a b\r\nc\r\n"));
        Assert.Empty(TextSketch.Lay(""));
        Assert.Empty(TextSketch.Lay("\r\n\r\n"));
    }
}
