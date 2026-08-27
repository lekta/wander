using Wander.Core.Search;
using Xunit;

namespace Wander.Core.Tests;

public class ContentMatcherTests {
    [Fact]
    public void TryMatch_FindsQuery_AndReportsLine() {
        string text = "first\nsecond\nthird has the word\nfourth";

        Assert.True(ContentMatcher.TryMatch(text, "the word", out string snippet, out int line));
        Assert.Equal(3, line);
        Assert.Equal("third has the word", snippet);
    }


    [Fact]
    public void TryMatch_Misses_ReturnsFalse() {
        Assert.False(ContentMatcher.TryMatch("nothing here", "absent", out string snippet, out int line));
        Assert.Equal("", snippet);
        Assert.Equal(0, line);
    }


    [Fact]
    public void TryMatch_EmptyQuery_ReturnsFalse() {
        // An empty query matches at offset zero under IndexOf, which would
        // make every file in a folder a hit.
        Assert.False(ContentMatcher.TryMatch("anything", "", out _, out _));
    }


    [Fact]
    public void TryMatch_IgnoresCase_InCyrillic() {
        // The whole point of decoding before searching: a query typed in
        // one case has to find the word written in another, in any alphabet.
        Assert.True(ContentMatcher.TryMatch("Привет, мир", "ПРИВЕТ", out string snippet, out _));
        Assert.Equal("Привет, мир", snippet);
    }


    [Fact]
    public void TryMatch_CollapsesWhitespace() {
        Assert.True(ContentMatcher.TryMatch("\t\tvalue   =\t42", "42", out string snippet, out _));
        Assert.Equal("value = 42", snippet);
    }


    [Fact]
    public void TryMatch_LongLine_IsCutAroundTheMatch() {
        // A minified file is one line and megabytes long; the snippet has
        // to be a window on it, not the line.
        string line = new string('a', 5000) + "needle" + new string('b', 5000);

        Assert.True(ContentMatcher.TryMatch(line, "needle", out string snippet, out int number));
        Assert.Equal(1, number);
        Assert.Contains("needle", snippet);
        Assert.True(snippet.Length <= ContentMatcher.SnippetLength + 2, $"snippet was {snippet.Length} chars");
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
    }


    [Fact]
    public void TryMatch_ShortLine_HasNoEllipsis() {
        Assert.True(ContentMatcher.TryMatch("short line with needle", "needle", out string snippet, out _));
        Assert.DoesNotContain("…", snippet);
    }


    [Fact]
    public void TryMatch_MatchOnFirstLine_IsLineOne() {
        Assert.True(ContentMatcher.TryMatch("needle\nrest", "needle", out _, out int line));
        Assert.Equal(1, line);
    }


    [Fact]
    public void TryMatch_CrLfFile_CountsLinesOnce() {
        // \r\n is two characters and one line break; counting \r as well
        // would double every line number in a Windows file.
        Assert.True(ContentMatcher.TryMatch("one\r\ntwo\r\nthree", "three", out _, out int line));
        Assert.Equal(3, line);
    }


    [Fact]
    public void Contains_IsCaseInsensitive() {
        Assert.True(ContentMatcher.Contains("MixedCase", "mixedcase"));
        Assert.False(ContentMatcher.Contains("MixedCase", "other"));
    }
}
