using Wander.Core.Imaging;

namespace Wander.Core.Tests.Imaging;

public class CanonAfInfoTests {
    /// <summary>
    /// A record the way an EOS R8 writes one, shrunk to four areas: the
    /// header, then widths, heights, X, Y, the in-focus bits and the
    /// selected bits.
    /// </summary>
    private static ushort[] Record(int valid, short[] w, short[] h, short[] x, short[] y, ushort focusBits) {
        int n = w.Length;
        var words = new List<ushort> { (ushort)(2 * (8 + 4 * n + 2)), 10, (ushort)n, (ushort)valid, 6000, 4000, 6000, 4000 };
        foreach (var run in new[] { w, h, x, y }) {
            words.AddRange(run.Select(v => unchecked((ushort)v)));
        }
        words.Add(focusBits);
        words.Add(focusBits);

        return words.ToArray();
    }


    [Fact]
    public void OneArea_InShares_OfTheAfFrame_YDown() {
        var record = Record(1, [0, 200, 0, 0], [0, 300, 0, 0], [0, 600, 0, 0], [0, -400, 0, 0], focusBits: 0b0010);

        var point = Assert.Single(CanonAfInfo.Parse(record));

        Assert.Equal(0.6, point.X, 9);
        Assert.Equal(0.4, point.Y, 9);
        Assert.Equal(200 / 6000.0, point.W, 9);
        Assert.Equal(300 / 4000.0, point.H, 9);
        Assert.True(point.InFocus);
    }


    [Fact]
    public void NegativeX_IsLeftOfTheMiddle() {
        var record = Record(1, [100, 0], [100, 0], [-600, 0], [0, 0], focusBits: 0);

        var point = Assert.Single(CanonAfInfo.Parse(record));

        Assert.Equal(0.4, point.X, 9);
        Assert.False(point.InFocus);
    }


    /// <summary>A frame focused by hand: the record is there, with nothing valid in it.</summary>
    [Fact]
    public void NothingValid_NoAreas() {
        var record = Record(0, [0, 0], [0, 0], [0, 0], [0, 0], focusBits: 0);

        Assert.Empty(CanonAfInfo.Parse(record));
    }


    [Fact]
    public void CutShort_NoAreas() {
        var record = Record(1, [100, 0], [100, 0], [0, 0], [0, 0], focusBits: 1);

        Assert.Empty(CanonAfInfo.Parse(record.Take(12).ToArray()));
        Assert.Empty(CanonAfInfo.Parse(Array.Empty<ushort>()));
    }
}
