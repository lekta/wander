using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class UnifiedDiffTests {
    private static DiffLineKind[] Classify(string text) {
        return UnifiedDiff.Classify(text.Split('\n'));
    }


    [Fact]
    public void GitPatch_HeadersBlocksAndLines() {
        var kinds = Classify(
            "diff --git a/readme.txt b/readme.txt\n" +
            "index 1234567..89abcde 100644\n" +
            "--- a/readme.txt\n" +
            "+++ b/readme.txt\n" +
            "@@ -1,3 +1,3 @@ title\n" +
            " first\n" +
            "-second\n" +
            "+second, changed\n" +
            " third");

        Assert.Equal(new[] {
            DiffLineKind.FileHeader, DiffLineKind.FileHeader, DiffLineKind.FileHeader, DiffLineKind.FileHeader,
            DiffLineKind.Hunk, DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added, DiffLineKind.Context,
        }, kinds);
    }

    [Fact]
    public void RemovedLineThatLooksLikeHeader_StaysRemoved() {
        // "-- comment" removed reads "--- comment"; the block still has a
        // line to go, so it is not the next file's header.
        var kinds = Classify(
            "@@ -1,2 +1,1 @@\n" +
            "--- comment\n" +
            " select 1;\n" +
            "--- b.sql\n" +
            "+++ b.sql");

        Assert.Equal(new[] {
            DiffLineKind.Hunk, DiffLineKind.Removed, DiffLineKind.Context,
            DiffLineKind.FileHeader, DiffLineKind.FileHeader,
        }, kinds);
    }

    [Fact]
    public void NoNewlineNote_AfterTheBlock() {
        var kinds = Classify(
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "\\ No newline at end of file\n" +
            "+new\n" +
            "\\ No newline at end of file");

        Assert.Equal(new[] {
            DiffLineKind.Hunk, DiffLineKind.Removed, DiffLineKind.Note, DiffLineKind.Added, DiffLineKind.Note,
        }, kinds);
    }

    [Fact]
    public void MailPatch_MessageIsPlain() {
        var kinds = Classify(
            "From 1234 Mon Sep 17 00:00:00 2001\n" +
            "Subject: [PATCH] Fix\n" +
            "\n" +
            "---\n" +
            " a.txt | 2 +-\n" +
            "diff --git a/a.txt b/a.txt\n" +
            "@@ -0,0 +1 @@\n" +
            "+added\n" +
            "-- \n" +
            "2.45.0");

        Assert.Equal(new[] {
            DiffLineKind.Plain, DiffLineKind.Plain, DiffLineKind.Plain, DiffLineKind.Plain, DiffLineKind.Plain,
            DiffLineKind.FileHeader, DiffLineKind.Hunk, DiffLineKind.Added, DiffLineKind.Plain, DiffLineKind.Plain,
        }, kinds);
    }

    [Fact]
    public void EmptyContextLine_CountsAsContext() {
        var kinds = Classify("@@ -1,3 +1,3 @@\n a\n\n b\nafter");

        Assert.Equal(new[] {
            DiffLineKind.Hunk, DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Plain,
        }, kinds);
    }

    [Theory]
    [InlineData("@@ -1,3 +1,4 @@", 3, 4)]
    [InlineData("@@ -7 +7 @@ void Main()", 1, 1)]
    [InlineData("@@ -0,0 +1,2 @@", 0, 2)]
    public void TryReadHunk_Counts(string line, int oldLines, int newLines) {
        Assert.True(UnifiedDiff.TryReadHunk(line, out int o, out int n));
        Assert.Equal(oldLines, o);
        Assert.Equal(newLines, n);
    }

    [Theory]
    [InlineData("@@ broken @@")]
    [InlineData("@@ -1,2 @@")]
    [InlineData("@@ -1,2 +3,4")]
    [InlineData("@@@ -1,2 -1,2 +1,3 @@@")]
    public void TryReadHunk_RejectsWhatIsNotOne(string line) {
        Assert.False(UnifiedDiff.TryReadHunk(line, out _, out _));
    }

    [Theory]
    [InlineData(".diff", true)]
    [InlineData(".PATCH", true)]
    [InlineData(".txt", false)]
    [InlineData(null, false)]
    public void IsPatchExtension(string? extension, bool expected) {
        Assert.Equal(expected, UnifiedDiff.IsPatchExtension(extension));
    }
}
