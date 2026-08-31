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
}
