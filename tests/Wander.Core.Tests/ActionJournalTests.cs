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
    public void AWarningAndAnError_CarryTheirMarks_AndNewsNone() {
        var journal = new ActionJournal();

        journal.Note("Скопировано: 3", _at);
        journal.Note("В корзину отправлено: 1, с ошибкой: 1", _at, StatusSeverity.Warning);
        journal.Note("Не удалось создать: нет доступа", _at, StatusSeverity.Error);

        Assert.Equal(
            new[] {
                "2026-09-03 14:23:05  Скопировано: 3",
                "2026-09-03 14:23:05  \u26A0 В корзину отправлено: 1, с ошибкой: 1",
                "2026-09-03 14:23:05  \u2716 Не удалось создать: нет доступа",
            },
            journal.Render().Split(Environment.NewLine));
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
    public void AnEntry_HasItsLinesIndentedUnderTheHeadline() {
        var journal = new ActionJournal();

        journal.Record(new JournalEntry(_at, JournalKind.Copied, "Скопировано в E:\\Video — 2 файла",
            new[] { "a.mov", "b.mov → b (1).mov" }));

        Assert.Equal(
            new[] {
                "2026-09-03 14:23:05  Скопировано в E:\\Video — 2 файла",
                "  a.mov",
                "  b.mov → b (1).mov",
            },
            journal.Render().Split(Environment.NewLine));
    }

    [Fact]
    public void ALongEntry_ShowsTheFirstLines_AndCountsTheRest() {
        var journal = new ActionJournal();
        var lines = Enumerable.Range(0, ActionJournal.LinesShown + 7).Select(i => $"f{i}.jpg").ToList();

        journal.Record(new JournalEntry(_at, JournalKind.Copied, "Скопировано", lines));

        var rendered = journal.Render("… ещё {0}").Split(Environment.NewLine);
        Assert.Equal(1 + ActionJournal.LinesShown + 1, rendered.Length);
        Assert.Equal($"  f{ActionJournal.LinesShown - 1}.jpg", rendered[^2]);
        Assert.Equal("  … ещё 7", rendered[^1]);
    }

    [Fact]
    public void TheSameOperationTwice_IsRecordedTwice() {
        var journal = new ActionJournal();
        var entry = new JournalEntry(_at, JournalKind.Copied, "Скопировано в D:\\ — 1 файл", new[] { "a.txt" });

        journal.Record(entry);
        journal.Record(entry);

        Assert.Equal(2, journal.Count);
    }

    [Fact]
    public void TheLimit_CountsEntries_NotLines() {
        var journal = new ActionJournal();
        for (int i = 0; i < 499; i++) {
            journal.Note($"line {i}", _at.AddSeconds(i));
        }

        journal.Record(new JournalEntry(_at, JournalKind.Moved, "Перемещено", new[] { "a", "b", "c" }));

        Assert.Equal(500, journal.Count);
        Assert.Contains("line 0", journal.Render());
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
