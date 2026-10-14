using Wander.Core.Search;

namespace Wander.Core.Tests;

public class SearchExpressionTests {
    [Fact]
    public void Parse_WithoutColon_IsAllName() {
        // What the box has always meant, and still does.
        var (name, text) = SearchExpression.Parse("report");

        Assert.Equal("report", name);
        Assert.Equal("", text);
    }


    [Fact]
    public void Parse_SplitsAtTheColon() {
        var (name, text) = SearchExpression.Parse("*.cs:budget");

        Assert.Equal("*.cs", name);
        Assert.Equal("budget", text);
    }


    [Fact]
    public void Parse_LeadingColon_IsTextOnly() {
        var (name, text) = SearchExpression.Parse(":budget");

        Assert.Equal("", name);
        Assert.Equal("budget", text);
    }


    [Fact]
    public void Parse_TrailingColon_IsNameOnly() {
        var (name, text) = SearchExpression.Parse("report:");

        Assert.Equal("report", name);
        Assert.Equal("", text);
    }


    [Fact]
    public void Parse_LaterColonsBelongToTheText() {
        // The reason the separator is the first colon and not every colon:
        // a URL in the text half has to survive being typed.
        var (name, text) = SearchExpression.Parse(":http://example.com");

        Assert.Equal("", name);
        Assert.Equal("http://example.com", text);
    }


    [Fact]
    public void Parse_Empty_IsBothEmpty() {
        Assert.Equal(("", ""), SearchExpression.Parse(""));
        Assert.Equal(("", ""), SearchExpression.Parse(null));
    }


    [Fact]
    public void Format_WithoutText_OmitsTheColon() {
        // An ordinary name filter must still read as the plain word it is.
        Assert.Equal("report", SearchExpression.Format("report", ""));
        Assert.Equal("", SearchExpression.Format("", ""));
    }


    [Fact]
    public void Format_WithText_ShowsBothHalves() {
        Assert.Equal("*.cs:budget", SearchExpression.Format("*.cs", "budget"));
        Assert.Equal(":budget", SearchExpression.Format("", "budget"));
    }


    [Theory]
    [InlineData("report")]
    [InlineData("*.cs:budget")]
    [InlineData(":budget")]
    [InlineData(":http://example.com")]
    [InlineData("")]
    public void RoundTrips(string expression) {
        var (name, text) = SearchExpression.Parse(expression);

        Assert.Equal(expression, SearchExpression.Format(name, text));
    }


    // --- The type in the name half -------------------------------------

    [Theory]
    [InlineData("report", "report", "")]
    [InlineData("/images", "", "images")]
    [InlineData("holiday /video", "holiday", "video")]
    [InlineData("holiday/ video ", "holiday", "video")]
    [InlineData("/", "", "")]
    [InlineData("", "", "")]
    public void SplitType_SlashParts_NameFromType(string head, string name, string type) {
        Assert.Equal((name, type), SearchExpression.SplitType(head));
    }


    [Fact]
    public void Parse_SlashAfterTheColon_BelongsToTheText() {
        // The type is looked for in the name half only: a path or a URL in
        // the text half must not turn into a type.
        var (head, text) = SearchExpression.Parse("*.cs:a/b");

        Assert.Equal(("*.cs", ""), SearchExpression.SplitType(head));
        Assert.Equal("a/b", text);
    }


    [Fact]
    public void JoinType_SpaceBeforeTheSlash_OnlyAfterAName() {
        Assert.Equal("holiday /video", SearchExpression.JoinType("holiday", "video"));
        Assert.Equal("/video", SearchExpression.JoinType("", "video"));
        Assert.Equal("holiday", SearchExpression.JoinType("holiday", ""));
    }


    [Theory]
    [InlineData("/im", 1, 3)]
    [InlineData("x /im:text", 3, 5)]
    [InlineData("x /", 3, 3)]
    public void TypeSpan_FromPastTheSlash_ToTheColonOrTheEnd(string expression, int start, int end) {
        Assert.Equal((start, end), SearchExpression.TypeSpan(expression));
    }


    [Theory]
    [InlineData("report")]
    [InlineData(":a/b")]
    [InlineData("")]
    public void TypeSpan_NoSlashInTheNameHalf_IsNull(string expression) {
        Assert.Null(SearchExpression.TypeSpan(expression));
    }


    [Fact]
    public void WithType_ReplacesTheWord_KeepsTheRest() {
        Assert.Equal(("x /images:text", 9), SearchExpression.WithType("x /im:text", "images"));
        Assert.Equal(("/images", 7), SearchExpression.WithType("/", "images"));
        Assert.Equal(("report", 6), SearchExpression.WithType("report", "images"));
    }
}
