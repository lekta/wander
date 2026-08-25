using System.Text;
using Wander.Core.Companions;

namespace Wander.Core.Tests;

public class Pp3SidecarTests {
    // Shaped like what RawTherapee actually writes: a [Version] header, the
    // rating in [General], and develop parameters after it. The whole point
    // of the tests below is that everything except one line survives.
    private const string Sample =
        "[Version]\nAppVersion=5.9\nVersion=346\n\n" +
        "[General]\nRank=2\nColorLabel=3\nInTrash=false\n\n" +
        "[Exposure]\nAuto=false\nCompensation=0.35\n";


    private static byte[] Utf8(string text) {
        return new UTF8Encoding(false).GetBytes(text);
    }

    private static string Text(byte[] bytes) {
        return new UTF8Encoding(false).GetString(bytes);
    }


    // --- Reading -------------------------------------------------------

    [Fact]
    public void Read_PicksUpRankAndColorLabel() {
        var rating = Pp3Sidecar.Read(Utf8(Sample));

        Assert.Equal(2, rating.Rank);
        Assert.Equal(3, rating.ColorLabel);
    }

    [Fact]
    public void Read_IgnoresKeysOutsideGeneral() {
        var rating = Pp3Sidecar.Read(Utf8("[Exposure]\nRank=5\n"));

        Assert.Null(rating.Rank);
    }

    [Fact]
    public void Read_HandlesMissingKeys() {
        var rating = Pp3Sidecar.Read(Utf8("[General]\nInTrash=false\n"));

        Assert.Null(rating.Rank);
        Assert.Null(rating.ColorLabel);
    }


    // --- Writing: the paranoid half ------------------------------------

    [Fact]
    public void WithRank_ChangesOnlyTheRankLine() {
        string updated = Text(Pp3Sidecar.WithRank(Utf8(Sample), 5));

        Assert.Equal(Sample.Replace("Rank=2", "Rank=5"), updated);
    }

    [Fact]
    public void WithRank_PreservesCrLfEndings() {
        string crlf = Sample.Replace("\n", "\r\n");

        string updated = Text(Pp3Sidecar.WithRank(Utf8(crlf), 4));

        Assert.Equal(crlf.Replace("Rank=2", "Rank=4"), updated);
    }

    [Fact]
    public void WithRank_PreservesUtf8Bom() {
        byte[] withBom = new UTF8Encoding(true).GetPreamble().Concat(Utf8(Sample)).ToArray();

        byte[] updated = Pp3Sidecar.WithRank(withBom, 1);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, updated.Take(3));
        Assert.Equal(1, Pp3Sidecar.Read(updated).Rank);
    }

    [Fact]
    public void WithRank_AddsTheKey_WhenGeneralHasNone() {
        string source = "[General]\nInTrash=false\n\n[Exposure]\nAuto=false\n";

        byte[] updated = Pp3Sidecar.WithRank(Utf8(source), 3);

        Assert.Equal(3, Pp3Sidecar.Read(updated).Rank);
        Assert.Contains("InTrash=false", Text(updated));
        Assert.Contains("Auto=false", Text(updated));
    }

    [Fact]
    public void WithRank_AddsTheSection_WhenTheFileHasNone() {
        byte[] updated = Pp3Sidecar.WithRank(Utf8("[Exposure]\nAuto=false\n"), 2);

        Assert.Equal(2, Pp3Sidecar.Read(updated).Rank);
        Assert.Contains("Auto=false", Text(updated));
    }

    [Fact]
    public void WithRank_RoundTrips_EveryAllowedValue() {
        for (int rank = 0; rank <= Pp3Sidecar.MaxRank; rank++) {
            Assert.Equal(rank, Pp3Sidecar.Read(Pp3Sidecar.WithRank(Utf8(Sample), rank)).Rank);
        }
    }

    [Fact]
    public void WithRank_RejectsValuesOutsideTheScale() {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pp3Sidecar.WithRank(Utf8(Sample), 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pp3Sidecar.WithRank(Utf8(Sample), -1));
    }
}
