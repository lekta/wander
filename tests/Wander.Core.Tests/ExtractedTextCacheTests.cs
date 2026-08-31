using Wander.Core.Search;

namespace Wander.Core.Tests;

public class ExtractedTextCacheTests {
    private static readonly DateTime _modified = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void Get_AfterPut_ReturnsText() {
        var cache = new ExtractedTextCache();
        cache.Put(@"C:\doc.docx", 100, _modified, "hello");

        Assert.Equal("hello", cache.Get(@"C:\doc.docx", 100, _modified));
    }


    [Fact]
    public void Get_UnknownPath_ReturnsNull() {
        var cache = new ExtractedTextCache();

        Assert.Null(cache.Get(@"C:\doc.docx", 100, _modified));
    }


    [Fact]
    public void Get_IsCaseInsensitiveOnPath() {
        var cache = new ExtractedTextCache();
        cache.Put(@"C:\Doc.docx", 100, _modified, "hello");

        Assert.Equal("hello", cache.Get(@"c:\doc.DOCX", 100, _modified));
    }


    [Fact]
    public void Get_AfterEdit_Misses() {
        // The whole reason the key carries size and time: an edited file
        // must not answer with what it used to say.
        var cache = new ExtractedTextCache();
        cache.Put(@"C:\doc.docx", 100, _modified, "before");

        Assert.Null(cache.Get(@"C:\doc.docx", 100, _modified.AddSeconds(1)));
        Assert.Null(cache.Get(@"C:\doc.docx", 101, _modified));
    }


    [Fact]
    public void Put_OverBudget_EvictsLeastRecentlyUsed() {
        // Budget of 8 bytes = 4 chars.
        var cache = new ExtractedTextCache(budgetBytes: 8);
        cache.Put(@"C:\a", 1, _modified, "aa");
        cache.Put(@"C:\b", 1, _modified, "bb");

        // Touching a makes b the oldest.
        Assert.Equal("aa", cache.Get(@"C:\a", 1, _modified));

        cache.Put(@"C:\c", 1, _modified, "cc");

        Assert.Equal("aa", cache.Get(@"C:\a", 1, _modified));
        Assert.Equal("cc", cache.Get(@"C:\c", 1, _modified));
        Assert.Null(cache.Get(@"C:\b", 1, _modified));
    }


    [Fact]
    public void Put_ItemLargerThanBudget_IsNotStored() {
        // Storing it would evict everything else to hold one entry that the
        // next insert throws away again.
        var cache = new ExtractedTextCache(budgetBytes: 8);
        cache.Put(@"C:\a", 1, _modified, "aa");
        cache.Put(@"C:\big", 1, _modified, new string('x', 100));

        Assert.Null(cache.Get(@"C:\big", 1, _modified));
        Assert.Equal("aa", cache.Get(@"C:\a", 1, _modified));
    }


    [Fact]
    public void Put_SamePathTwice_DoesNotDoubleCount() {
        var cache = new ExtractedTextCache();
        cache.Put(@"C:\a", 1, _modified, "aaaa");
        cache.Put(@"C:\a", 1, _modified, "bb");

        Assert.Equal(1, cache.Count);
        Assert.Equal(2, cache.CharCount);
        Assert.Equal("bb", cache.Get(@"C:\a", 1, _modified));
    }


    [Fact]
    public void Clear_DropsEverything() {
        var cache = new ExtractedTextCache();
        cache.Put(@"C:\a", 1, _modified, "aa");
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CharCount);
        Assert.Null(cache.Get(@"C:\a", 1, _modified));
    }


    [Fact]
    public void CharCount_StaysWithinBudget() {
        var cache = new ExtractedTextCache(budgetBytes: 64);
        for (int i = 0; i < 100; i++) {
            cache.Put($@"C:\f{i}", 1, _modified, new string('x', 10));
        }

        Assert.True(cache.CharCount <= 32, $"held {cache.CharCount} chars against a 32-char budget");
    }
}
