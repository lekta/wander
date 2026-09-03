using Wander.Core.Logging;

namespace Wander.Core.Tests;

public class ActionJournalTests {
    private static readonly DateTime _at = new(2026, 9, 3, 14, 23, 5, DateTimeKind.Local);


    [Fact]
    public void ALineIsKeptWithItsTime() {
        var journal = new ActionJournal();

        journal.Note("Скопировано элементов: 3", _at);

        Assert.Equal(1, journal.Count);
        Assert.Equal("2026-09-03 14:23:05  Скопировано элементов: 3", journal.Render());
    }

    [Fact]
    public void LinesComeBackOldestFirst() {
        var journal = new ActionJournal();

        journal.Note("first", _at);
        journal.Note("second", _at.AddSeconds(5));

        Assert.Equal(
            "2026-09-03 14:23:05  first" + Environment.NewLine + "2026-09-03 14:23:10  second",
            journal.Render());
    }

    /// <summary>
    /// The status bar is rewritten constantly — every selection change puts
    /// the item count back. A journal of two hundred identical lines
    /// answers nothing.
    /// </summary>
    [Fact]
    public void TheSameLineTwiceRunning_IsNotedOnce() {
        var journal = new ActionJournal();

        journal.Note("Элементов: 12", _at);
        journal.Note("Элементов: 12", _at.AddSeconds(1));

        Assert.Equal(1, journal.Count);
    }

    [Fact]
    public void ALineThatComesBackLater_IsNotedAgain() {
        var journal = new ActionJournal();

        journal.Note("Элементов: 12", _at);
        journal.Note("Скопировано элементов: 1", _at.AddSeconds(1));
        journal.Note("Элементов: 12", _at.AddSeconds(2));

        Assert.Equal(3, journal.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToSay_IsNotNoted(string? text) {
        var journal = new ActionJournal();

        journal.Note(text, _at);

        Assert.Equal(0, journal.Count);
        Assert.Equal("", journal.Render());
    }

    [Fact]
    public void ALongSession_DropsTheOldestLines() {
        var journal = new ActionJournal();
        for (int i = 0; i < 600; i++) {
            journal.Note($"line {i}", _at.AddSeconds(i));
        }

        Assert.Equal(500, journal.Count);
        string text = journal.Render();
        Assert.DoesNotContain("line 99 ", text + " ");
        Assert.Contains("line 599", text);
    }
}
