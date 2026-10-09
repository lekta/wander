using Wander.Core.Appearance;

namespace Wander.Core.Tests;

public class ToneTests {
    private static readonly Rgb _white = new(255, 255, 255);
    private static readonly Rgb _black = new(0, 0, 0);
    private static readonly Rgb _darkPaper = new(0x1E, 0x1E, 0x1E);


    [Fact]
    public void Contrast_RunsFromOneToTwentyOne() {
        Assert.Equal(21.0, Tone.Contrast(_black, _white), 1);
        Assert.Equal(1.0, Tone.Contrast(_darkPaper, _darkPaper), 3);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(110, true)]
    [InlineData(128, true)]
    [InlineData(141, false)]
    [InlineData(255, false)]
    public void IsDark_SplitsTheGreysAtTheMiddle(int level, bool dark) {
        Assert.Equal(dark, Tone.IsDark(Rgb.Grey(level)));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(110)]
    [InlineData(200)]
    public void Quietest_StillReachesTheTarget(int level) {
        var surface = Rgb.Grey(level);
        var ink = Tone.IsDark(surface) ? _white : _black;

        var tone = Tone.Quietest(ink, surface, 4.5);

        Assert.True(Tone.Contrast(tone, surface) >= 4.5);
        Assert.NotEqual(ink, tone);
    }

    [Fact]
    public void Readable_LeavesAColourThatAlreadyReads() {
        var amber = new Rgb(0xE0, 0xA0, 0x40);

        Assert.Equal(amber, Tone.Readable(amber, _darkPaper, 4.5));
    }

    [Theory]
    [InlineData(0x00, 0x00, 0xFF)]
    [InlineData(0x00, 0x00, 0x80)]
    [InlineData(0x00, 0x80, 0x00)]
    [InlineData(0xA3, 0x15, 0x15)]
    public void Readable_LightensAColourMadeForWhitePaper_KeepingItsHue(byte r, byte g, byte b) {
        var ink = new Rgb(r, g, b);

        var tone = Tone.Readable(ink, _darkPaper, 4.5);

        Assert.True(Tone.Contrast(tone, _darkPaper) >= 4.5);
        // The strongest channel stays the strongest: blue is still blue.
        Assert.Equal(Strongest(ink), Strongest(tone));
    }

    [Fact]
    public void Readable_DarkensOnALightSurface() {
        var pale = new Rgb(0xFF, 0xE0, 0x80);

        var tone = Tone.Readable(pale, _white, 4.5);

        Assert.True(Tone.Contrast(tone, _white) >= 4.5);
        Assert.True(tone.R < pale.R);
    }

    [Fact]
    public void Lift_LeansBlueAndClamps() {
        Assert.Equal(new Rgb(44, 44, 58), Tone.Lift(Rgb.Grey(30), 14, blue: 14));
        Assert.Equal(new Rgb(255, 255, 255), Tone.Lift(Rgb.Grey(250), 14, blue: 14));
    }


    private static int Strongest(Rgb c) {
        return c.R >= c.G && c.R >= c.B ? 0 : c.G >= c.B ? 1 : 2;
    }
}
