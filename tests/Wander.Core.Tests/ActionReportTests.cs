using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class ActionReportTests {
    private static readonly FakeTextSource _text = new(new Dictionary<string, string> {
        ["ActionReportCancelled"] = "{0}: cancelled",
        ["ActionReportError"] = "{0}: {1}",
        ["ActionReportExitCode"] = "{0}: code {1}: {2}",
        ["ActionReportExitCodeOnly"] = "{0}: code {1}",
        ["ActionReportMore"] = "{0} more",
        ["ActionReportOnce"] = "{0} files, one command: {1}",
    });


    [Fact]
    public void Lines_OneCommand_SaysTheSharedOutcomeOnce() {
        var results = new[] {
            new ActionItemResult(@"C:\v\a.mov", null, BatchItemStatus.Failed, 2, "boom\n", null),
            new ActionItemResult(@"C:\v\b.mov", null, BatchItemStatus.Failed, 2, "boom\n", null),
            new ActionItemResult(@"C:\v\c.mov", null, BatchItemStatus.Failed, 2, "boom\n", null),
        };

        Assert.Equal(new[] { "3 files, one command: a.mov: code 2: boom" }, ActionReport.Lines(results, text: _text, oneCommand: true));
        // One file in one command is one line as usual.
        Assert.Equal(new[] { "a.mov: code 2: boom" }, ActionReport.Lines(results.Take(1).ToArray(), text: _text, oneCommand: true));
    }


    [Fact]
    public void IsNeeded_OnlyWhenSomethingWasNotOk() {
        Assert.False(ActionReport.IsNeeded(new[] { Ok(@"C:\a.mov") }));
        Assert.True(ActionReport.IsNeeded(new[] { Ok(@"C:\a.mov"), Cancelled(@"C:\b.mov") }));
    }

    [Fact]
    public void Lines_NameEachFailure_WithCodeAndTheLastLineOfStderr() {
        var results = new[] {
            Ok(@"C:\v\a.mov"),
            new ActionItemResult(@"C:\v\b.mov", null, BatchItemStatus.Failed, 1,
                "ffmpeg version 7\r\nb.mov: Invalid data found when processing input\r\n", null),
            new ActionItemResult(@"C:\v\c.mov", null, BatchItemStatus.Failed, 3, "", null),
            new ActionItemResult(@"C:\v\d.mov", null, BatchItemStatus.Failed, 0, "gone",
                new IOException("Access denied")),
            Cancelled(@"C:\v\e.mov"),
        };

        var lines = ActionReport.Lines(results, text: _text);

        Assert.Equal(new[] {
            "b.mov: code 1: b.mov: Invalid data found when processing input",
            "c.mov: code 3",
            "d.mov: Access denied",
            "e.mov: cancelled",
        }, lines);
    }

    [Fact]
    public void Lines_AreCapped_AndTheRestCounted() {
        var results = Enumerable.Range(0, 25).Select(i => Cancelled($@"C:\v\{i}.mov")).ToArray();

        var lines = ActionReport.Lines(results, limit: 20, text: _text);

        Assert.Equal(21, lines.Count);
        Assert.Equal("19.mov: cancelled", lines[19]);
        Assert.Equal("5 more", lines[20]);
    }

    [Fact]
    public void Lines_NameAFolderByItsName_AndARootByItsPath() {
        var lines = ActionReport.Lines(new[] { Cancelled(@"C:\photos\2024\"), Cancelled(@"D:\") }, text: _text);

        Assert.Equal(new[] { "2024: cancelled", @"D:\: cancelled" }, lines);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("one", "one")]
    [InlineData("one\ntwo\n\n", "two")]
    [InlineData("one\r\ntwo  \r\n", "two")]
    public void LastLine_IsTheLastNonEmptyLine(string tail, string expected) {
        Assert.Equal(expected, ActionReport.LastLine(tail));
    }


    private static ActionItemResult Ok(string path) {
        return new ActionItemResult(path, null, BatchItemStatus.Ok, 0, string.Empty, null);
    }

    private static ActionItemResult Cancelled(string path) {
        return new ActionItemResult(path, null, BatchItemStatus.Cancelled, 0, string.Empty, null);
    }
}
