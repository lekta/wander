using Wander.Core.Imaging;

namespace Wander.Core.Tests;

public class SizedCacheTests {
    [Fact]
    public void OverCount_TheOldestGoes() {
        var cache = new SizedCache<string, int>(3, new MemoryShare(1000));
        cache.Put("a", 1, 10);
        cache.Put("b", 2, 10);
        cache.Put("c", 3, 10);

        cache.Put("d", 4, 10);

        Assert.False(cache.Contains("a"));
        Assert.Equal(3, cache.Count);
        Assert.Equal(30, cache.Held);
    }

    [Fact]
    public void TryGet_MakesAnEntryTheLatest() {
        var cache = new SizedCache<string, int>(2, new MemoryShare(1000));
        cache.Put("a", 1, 10);
        cache.Put("b", 2, 10);

        Assert.True(cache.TryGet("a", out int a));
        cache.Put("c", 3, 10);

        Assert.Equal(1, a);
        Assert.True(cache.Contains("a"));
        Assert.False(cache.Contains("b"));
    }

    /// <summary>Two panes' frames count together: one's put makes room from its own, not the other's.</summary>
    [Fact]
    public void OverTheShare_TheOldestOfThisCacheGoes_TheOtherCacheIsLeftAlone() {
        var share = new MemoryShare(100);
        var pane = new SizedCache<string, int>(3, share);
        var other = new SizedCache<string, int>(3, share);
        other.Put("x", 0, 40);
        pane.Put("a", 1, 30);

        pane.Put("b", 2, 40);

        Assert.False(pane.Contains("a"));
        Assert.True(other.Contains("x"));
        Assert.Equal(80, share.Held);
    }

    /// <summary>The picture on show stays, even alone over the limit: it is on screen.</summary>
    [Fact]
    public void KeptEntries_AndTheOneJustPut_NeverGo() {
        var share = new MemoryShare(50);
        var cache = new SizedCache<string, int>(3, share);
        cache.Keep(new[] { "shown" });
        cache.Put("shown", 1, 40);

        cache.Put("ahead", 2, 40);

        Assert.True(cache.Contains("shown"));
        Assert.True(cache.Contains("ahead"));
        Assert.Equal(80, share.Held);
    }

    /// <summary>
    /// A neighbour is decoded only when it fits beside what stays - the
    /// picture on show and the neighbour warmed before it - counting what
    /// this cache would drop: older frames, not kept.
    /// </summary>
    [Fact]
    public void Fits_CountsWhatWouldBeDropped_NotWhatIsKept() {
        var share = new MemoryShare(100);
        var cache = new SizedCache<string, int>(3, share);
        cache.Put("old", 0, 30);
        cache.Keep(new[] { "shown", "next", "previous" });
        cache.Put("shown", 1, 40);

        Assert.True(cache.Fits(60));
        Assert.False(cache.Fits(61));

        cache.Put("next", 2, 40);

        Assert.False(cache.Contains("old"));
        Assert.True(cache.Fits(20));
        Assert.False(cache.Fits(21));
    }

    [Fact]
    public void Fits_CountsTheOtherCachesOfTheShare() {
        var share = new MemoryShare(100);
        var cache = new SizedCache<string, int>(3, share);
        new SizedCache<string, int>(3, share).Put("x", 0, 70);

        Assert.True(cache.Fits(30));
        Assert.False(cache.Fits(31));
    }

    [Fact]
    public void Clear_GivesTheBytesBack() {
        var share = new MemoryShare(100);
        var cache = new SizedCache<string, int>(3, share);
        cache.Put("a", 1, 30);
        cache.Put("b", 2, 20);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Held);
        Assert.Equal(0, share.Held);
    }

    [Fact]
    public void PutAgain_ReplacesTheEntryAndItsBytes() {
        var share = new MemoryShare(100);
        var cache = new SizedCache<string, int>(3, share);
        cache.Put("a", 1, 30);

        cache.Put("a", 2, 50);

        Assert.True(cache.TryGet("a", out int value));
        Assert.Equal(2, value);
        Assert.Equal(1, cache.Count);
        Assert.Equal(50, share.Held);
    }

    [Fact]
    public void KeysCompareAsTheCacheWasTold() {
        var cache = new SizedCache<string, int>(3, new MemoryShare(100), StringComparer.OrdinalIgnoreCase);
        cache.Put(@"C:\A.jpg", 1, 10);

        Assert.True(cache.Contains(@"c:\a.JPG"));
    }
}
