using System.Text;
using Wander.Core.Preview;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class FileDiffTests {
    private const string Left = @"C:\a\notes.txt";
    private const string Right = @"C:\b\notes.txt";

    private const string GitAnswer =
        "diff --git a/C:/a/notes.txt b/C:/b/notes.txt\n" +
        "index 1234567..89abcde 100644\n" +
        "--- a/C:/a/notes.txt\n" +
        "+++ b/C:/b/notes.txt\n" +
        "@@ -1,2 +1,2 @@\n" +
        " first\n" +
        "-second\n" +
        "+второй\n";

    private readonly FakeFileSystem _fs = new();
    private readonly FakeDiffTool _tool = new();


    public FileDiffTests() {
        _fs.Files[Left] = Encoding.UTF8.GetBytes("first\nsecond\n");
        _fs.Files[Right] = Encoding.UTF8.GetBytes("first\nвторой\n");
    }


    private static DiffToolRun Run(string output, int exitCode = 1, bool cut = false) {
        return new DiffToolRun(exitCode, Encoding.UTF8.GetBytes(output), cut, "");
    }


    [Fact]
    public void Arguments_NoIndexAndPathsAfterDashes() {
        var args = FileDiff.Arguments(Left, Right).ToList();

        int diff = args.IndexOf("diff");
        Assert.Equal("--no-index", args[diff + 1]);
        Assert.All(args.Take(diff).Where((_, i) => i % 2 == 0), a => Assert.Equal("-c", a));
        Assert.Contains("core.autocrlf=false", args);
        Assert.Contains("--ignore-cr-at-eol", args);
        Assert.Contains("--no-ext-diff", args);
        Assert.Equal(new[] { "--", Left, Right }, args.TakeLast(3));
    }

    [Fact]
    public void Read_Differences_FileHeadersDropped() {
        var result = FileDiff.Read(Run(GitAnswer));

        Assert.Equal(FileDiffVerdict.Different, result.Verdict);
        Assert.StartsWith("@@ -1,2 +1,2 @@", result.Text);
        Assert.Contains("+второй", result.Text);
        Assert.False(result.Cut);
    }

    [Fact]
    public void Read_ExitZero_IsLineEndings() {
        // The bytes were found different before git was asked.
        Assert.Equal(FileDiffVerdict.LineEndingsOnly, FileDiff.Read(Run("", exitCode: 0)).Verdict);
    }

    [Fact]
    public void Read_ExitOneWithoutBlocks_IsLineEndings() {
        Assert.Equal(FileDiffVerdict.LineEndingsOnly, FileDiff.Read(Run("diff --git a/x b/x\n", exitCode: 1)).Verdict);
    }

    [Fact]
    public void Read_BinaryNote_IsNotText() {
        var result = FileDiff.Read(Run("Binary files a/C:/a/x.txt and b/C:/b/x.txt differ\n"));

        Assert.Equal(FileDiffVerdict.NotText, result.Verdict);
    }

    [Fact]
    public void Read_OtherExitCode_Fails() {
        var result = FileDiff.Read(new DiffToolRun(128, Array.Empty<byte>(), false, "fatal: bad"));

        Assert.Equal(FileDiffVerdict.Failed, result.Verdict);
        Assert.Contains("fatal: bad", result.Error);
    }

    [Fact]
    public void Read_Cut_TornLastLineDropped() {
        var bytes = Encoding.UTF8.GetBytes(GitAnswer + "+пол");
        // Half of the last letter: what a cut in the middle of UTF-8 leaves.
        var torn = bytes.AsSpan(0, bytes.Length - 1).ToArray();

        var result = FileDiff.Read(new DiffToolRun(-1, torn, true, ""));

        Assert.Equal(FileDiffVerdict.Different, result.Verdict);
        Assert.True(result.Cut);
        Assert.EndsWith("+второй\n", result.Text);
    }

    [Fact]
    public void Read_SingleByteCodepage_Decoded() {
        // An added Russian line in 1251, enough letters for the probe to decide.
        var hunk = Encoding.ASCII.GetBytes("@@ -1 +1 @@\n-old\n+");
        var cyrillic = "Привет, мир, это строка".Select(c => (byte)(c switch {
            >= 'А' and <= 'я' => 0xC0 + (c - 'А'),
            _ => c,
        })).ToArray();
        var output = hunk.Concat(cyrillic).Concat(new[] { (byte)'\n' }).ToArray();

        var result = FileDiff.Read(new DiffToolRun(1, output, false, ""));

        Assert.Contains("+Привет, мир, это строка", result.Text);
    }

    [Fact]
    public async Task Compare_SameBytes_IdenticalWithoutGit() {
        _fs.Files[Right] = _fs.Files[Left];

        var result = await new FileDiff(_fs, _tool).CompareAsync(Left, Right, CancellationToken.None);

        Assert.Equal(FileDiffVerdict.Identical, result.Verdict);
        Assert.Empty(_tool.Calls);
    }

    [Fact]
    public async Task Compare_Binary_NotTextWithoutGit() {
        _fs.Files[Right] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0 };

        var result = await new FileDiff(_fs, _tool).CompareAsync(Left, Right, CancellationToken.None);

        Assert.Equal(FileDiffVerdict.NotText, result.Verdict);
        Assert.Empty(_tool.Calls);
    }

    [Fact]
    public async Task Compare_OverLimit_TooLarge() {
        var result = await new FileDiff(_fs, _tool, maxFileBytes: 5).CompareAsync(Left, Right, CancellationToken.None);

        Assert.Equal(FileDiffVerdict.TooLarge, result.Verdict);
        Assert.Empty(_tool.Calls);
    }

    [Fact]
    public async Task Compare_BinaryOverLimit_IsNotText_NotTooLarge() {
        // Two big videos: the window has nothing to say about them.
        _fs.Files[Left] = new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70 };

        var result = await new FileDiff(_fs, _tool, maxFileBytes: 5).CompareAsync(Left, Right, CancellationToken.None);

        Assert.Equal(FileDiffVerdict.NotText, result.Verdict);
    }

    [Fact]
    public async Task Compare_NoGit_SaysSo() {
        _tool.IsAvailable = false;

        var result = await new FileDiff(_fs, _tool).CompareAsync(Left, Right, CancellationToken.None);

        Assert.Equal(FileDiffVerdict.NoTool, result.Verdict);
    }

    [Fact]
    public async Task Compare_Different_AsksGitAboutBoth() {
        _tool.Answer = Run(GitAnswer);

        var result = await new FileDiff(_fs, _tool).CompareAsync(Left, Right, CancellationToken.None);

        Assert.Equal(FileDiffVerdict.Different, result.Verdict);
        Assert.Equal(new[] { Left, Right }, Assert.Single(_tool.Calls).TakeLast(2));
    }

    [Fact]
    public async Task Compare_GitHangs_FailsAfterTimeout() {
        _tool.Hangs = true;

        var result = await new FileDiff(_fs, _tool, timeout: TimeSpan.FromMilliseconds(50))
            .CompareAsync(Left, Right, CancellationToken.None);

        Assert.Equal(FileDiffVerdict.Failed, result.Verdict);
    }

    [Fact]
    public async Task Compare_CallerCancels_Throws() {
        _tool.Hangs = true;
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new FileDiff(_fs, _tool).CompareAsync(Left, Right, cts.Token));
    }

    [Fact]
    public async Task Compare_MissingFile_Fails() {
        _fs.Files.Remove(Right);

        var result = await new FileDiff(_fs, _tool).CompareAsync(Left, Right, CancellationToken.None);

        Assert.Equal(FileDiffVerdict.Failed, result.Verdict);
    }
}
