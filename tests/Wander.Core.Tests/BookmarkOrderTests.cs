using Wander.Core.Navigation;

namespace Wander.Core.Tests;

public class BookmarkOrderTests {
    [Fact]
    public void AMove_PassesOneRow() {
        var list = new List<string> { "a", "b", "c" };

        Assert.True(BookmarkOrder.Move(list, "b", -1, _ => true));

        Assert.Equal(new[] { "b", "a", "c" }, list);
    }

    [Theory]
    [InlineData("a", -1)]
    [InlineData("c", 1)]
    [InlineData("nope", 1)]
    public void NoRowToPass_MovesNothing(string item, int delta) {
        var list = new List<string> { "a", "b", "c" };

        Assert.False(BookmarkOrder.Move(list, item, delta, _ => true));

        Assert.Equal(new[] { "a", "b", "c" }, list);
    }

    [Fact]
    public void InThePanel_AMove_PassesTheNextShownRow_NotAHiddenOne() {
        var list = new List<string> { "a", "hidden", "b" };

        Assert.True(BookmarkOrder.Move(list, "b", -1, item => item != "hidden"));

        Assert.Equal(new[] { "b", "a", "hidden" }, list);
    }

    [Fact]
    public void OnlyHiddenRowsAhead_MovesNothing() {
        var list = new List<string> { "a", "hidden" };

        Assert.False(BookmarkOrder.Move(list, "a", 1, item => item != "hidden"));

        Assert.Equal(new[] { "a", "hidden" }, list);
    }

    [Fact]
    public void ASavedOrder_IsKept_TheNewComeLast_TheGoneDrop() {
        string[] known = { "downloads", "documents", "bin" };

        var merged = BookmarkOrder.Merge(known, new[] { "bin", "old", "downloads", "BIN" });

        Assert.Equal(new[] { "bin", "downloads", "documents" }, merged);
    }

    [Fact]
    public void NoSavedOrder_IsTheUsualOne() {
        string[] known = { "downloads", "documents" };

        Assert.Equal(known, BookmarkOrder.Merge(known, null));
    }
}
