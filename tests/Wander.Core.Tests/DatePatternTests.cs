using System.Globalization;
using Wander.Core.Rename;

namespace Wander.Core.Tests;

public class DatePatternTests {
    private static readonly DateTime _at = new(2026, 5, 17, 14, 30, 9);


    [Theory]
    [InlineData("mm-dd", "05-17")]
    [InlineData("yyyy-mm-dd hh-mm", "2026-05-17 14-30")]
    [InlineData("mm-ss", "30-09")]
    [InlineData("hh.mm", "14.30")]
    [InlineData("dd.mm.yy", "17.05.26")]
    public void M_IsTheMonth_UnlessRightAfterTheHoursOrBeforeTheSeconds(string pattern, string expected) {
        Assert.Equal(expected, DatePattern.Format(_at, pattern));
    }

    [Fact]
    public void Case_DoesNotMatter() {
        Assert.Equal("2026-05-17 14-30", DatePattern.Format(_at, "YYYY-MM-DD HH-MM"));
    }

    [Theory]
    [InlineData("yyyy-MM-dd")]
    [InlineData("yyyyMMdd_HHmmss")]
    [InlineData("yyyy-MM-dd HH-mm")]
    public void ATemplateWrittenForDotNet_PrintsWhatItPrinted(string pattern) {
        Assert.Equal(_at.ToString(pattern, CultureInfo.InvariantCulture), DatePattern.Format(_at, pattern));
    }

    [Fact]
    public void OneLetter_HasNoLeadingZero() {
        var early = new DateTime(2026, 1, 2, 3, 4, 5);

        Assert.Equal("2.1.26 3-4-5", DatePattern.Format(early, "d.m.y h-m-s"));
    }

    [Fact]
    public void EverythingElse_StandsAsItIs() {
        Assert.Equal("20260517T1430 (фото)", DatePattern.Format(_at, "yyyymmddThhmm (фото)"));
    }
}
