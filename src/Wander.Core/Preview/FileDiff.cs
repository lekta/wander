using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// The program that tells two files apart - <c>git diff --no-index</c>.
/// Starting it is the platform's business; what to ask it and what its
/// answer means is <see cref="FileDiff"/>'s.
/// </summary>
public interface IDiffTool {
    /// <summary>Whether the program is on this machine at all. May probe the disk the first time.</summary>
    bool IsAvailable { get; }

    /// <summary>Runs the program and collects what it prints, at most <paramref name="maxOutput"/> bytes of it.</summary>
    /// <exception cref="OperationCanceledException">Cancelled; the program is stopped.</exception>
    Task<DiffToolRun> RunAsync(IReadOnlyList<string> arguments, int maxOutput, CancellationToken ct);
}


/// <summary>What the diff program answered.</summary>
/// <param name="ExitCode">Its exit code; meaningless when <paramref name="Cut"/>.</param>
/// <param name="Output">stdout, as bytes - the files' own encoding is in there.</param>
/// <param name="Cut">stdout went on past the limit, and the program was stopped there.</param>
/// <param name="Error">The end of stderr, for the log.</param>
public sealed record DiffToolRun(int ExitCode, byte[] Output, bool Cut, string Error);


public enum FileDiffVerdict {
    /// <summary>The differences are in <see cref="FileDiffResult.Text"/>.</summary>
    Different,
    Identical,
    /// <summary>Only CRLF against LF: every line would read as changed, so they are not shown.</summary>
    LineEndingsOnly,
    /// <summary>One of the two is not text; side by side is all there is.</summary>
    NotText,
    /// <summary>One of the two is over <see cref="FileDiff.MaxFileBytes"/>.</summary>
    TooLarge,
    /// <summary>No git on this machine.</summary>
    NoTool,
    Failed,
}


/// <param name="Text">The differences, block headers on, file headers off - the window names the files.</param>
/// <param name="Cut">Only the start of the differences is in <paramref name="Text"/>.</param>
/// <param name="Error">What went wrong, for the log; never shown.</param>
public sealed record FileDiffResult(FileDiffVerdict Verdict, string Text = "", bool Cut = false, string? Error = null);


/// <summary>
/// Two text files compared line by line (decision of 2026-11-02: git, one
/// column, the differences as a patch). The cheap answers come first and
/// without a program: not text, too big, the same bytes. Then
/// <c>git diff --no-index</c>, which is fast on big files and needs no
/// repository; its answer is read here.
///
/// <para>
/// Line endings are ignored (<c>--ignore-cr-at-eol</c>): a file saved with
/// CRLF against the same file with LF would show every line removed and
/// added again. That difference is still said - the bytes differ and git
/// found nothing, <see cref="FileDiffVerdict.LineEndingsOnly"/>.
/// </para>
/// </summary>
public sealed class FileDiff {
    /// <summary>The comparison of the conflict window's own pairs stops here too.</summary>
    public const long MaxFileBytes = FileContentComparer.AutoCompareLimit;

    /// <summary>As much of the differences as the text preview shows of a text.</summary>
    public const int MaxShownBytes = 1024 * 1024;

    /// <summary>Longer than git takes on two 64 MB files; a program that hangs is let go here.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IFileSystem _fs;
    private readonly IDiffTool? _tool;
    private readonly long _maxFileBytes;
    private readonly TimeSpan _timeout;


    public FileDiff(IFileSystem fs, IDiffTool? tool, long maxFileBytes = MaxFileBytes, TimeSpan? timeout = null) {
        _fs = fs;
        _tool = tool;
        _maxFileBytes = maxFileBytes;
        _timeout = timeout ?? DefaultTimeout;
    }


    /// <summary>
    /// How <paramref name="left"/> becomes <paramref name="right"/>. The reads
    /// and the program run off the calling thread.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> fired.</exception>
    public async Task<FileDiffResult> CompareAsync(string left, string right, CancellationToken ct) {
        var early = await Task.Run(() => Probe(left, right, ct), ct).ConfigureAwait(false);
        if (early is not null) {
            return early;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        DiffToolRun run;
        try {
            run = await _tool!.RunAsync(Arguments(left, right), MaxShownBytes, timeout.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return new FileDiffResult(FileDiffVerdict.Failed, Error: $"git did not answer in {_timeout.TotalSeconds:0} s");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new FileDiffResult(FileDiffVerdict.Failed, Error: ex.Message);
        }

        return Read(run);
    }

    /// <summary>
    /// What git is asked. No colour, no external diff program or text
    /// conversion from the user's config - the output is read, not shown
    /// raw; no line-ending conversion either (<c>core.autocrlf</c> warns on
    /// every file, and with <c>safecrlf</c> may refuse) - the CR is ignored
    /// by the diff itself. Paths after <c>--</c>, so a name starting with a
    /// dash is a name.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string left, string right) {
        return new[] {
            "-c", "core.quotepath=off", "-c", "core.autocrlf=false", "-c", "core.safecrlf=false",
            "diff", "--no-index", "--no-color", "--no-ext-diff", "--no-textconv", "--ignore-cr-at-eol",
            "--", left, right,
        };
    }

    /// <summary>
    /// What git's answer means. Exit code 1 is "they differ", not a
    /// failure; 0 after the bytes were found different is the line endings.
    /// The file headers are dropped: the paths are long, and the window
    /// shows the two files already.
    /// </summary>
    public static FileDiffResult Read(DiffToolRun run) {
        if (!run.Cut && run.ExitCode is not (0 or 1)) {
            return new FileDiffResult(FileDiffVerdict.Failed, Error: $"git exited with {run.ExitCode}: {run.Error}");
        }

        byte[] output = run.Output;
        int start = FirstHunk(output);
        if (start < 0) {
            return HasBinaryNote(output)
                ? new FileDiffResult(FileDiffVerdict.NotText)
                : new FileDiffResult(FileDiffVerdict.LineEndingsOnly);
        }

        int end = output.Length;
        if (run.Cut) {
            // The last line is likely torn - a half of a UTF-8 letter among it.
            int lastBreak = Array.LastIndexOf(output, (byte)'\n', end - 1);
            end = lastBreak >= start ? lastBreak + 1 : end;
        }

        string text = EncodingProbe.Decode(output.AsSpan(start, end - start));

        return new FileDiffResult(FileDiffVerdict.Different, text, run.Cut);
    }


    /// <summary>
    /// The answers that need no program; null when git has to be asked.
    /// Text first: two big videos are not "too big to compare", they are
    /// nothing a text comparison has a word about.
    /// </summary>
    private FileDiffResult? Probe(string left, string right, CancellationToken ct) {
        try {
            long biggest = 0;
            foreach (string path in new[] { left, right }) {
                using var stream = _fs.OpenRead(path);
                var sample = new byte[TextProbe.SampleSize];
                int read = stream.ReadAtLeast(sample, sample.Length, throwOnEndOfStream: false);
                if (!TextProbe.LooksLikeText(sample.AsSpan(0, read))) {
                    return new FileDiffResult(FileDiffVerdict.NotText);
                }
                if (stream.CanSeek) {
                    biggest = Math.Max(biggest, stream.Length);
                }
            }

            if (biggest > _maxFileBytes) {
                return new FileDiffResult(FileDiffVerdict.TooLarge);
            }
            if (FileContentComparer.AreIdentical(_fs, left, right, ct)) {
                return new FileDiffResult(FileDiffVerdict.Identical);
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return new FileDiffResult(FileDiffVerdict.Failed, Error: ex.Message);
        }

        return _tool is { IsAvailable: true } ? null : new FileDiffResult(FileDiffVerdict.NoTool);
    }

    /// <summary>Where the first <c>@@</c> line starts, or -1.</summary>
    private static int FirstHunk(byte[] output) {
        ReadOnlySpan<byte> marker = "@@ -"u8;
        int at = 0;
        while (at < output.Length) {
            if (output.AsSpan(at).StartsWith(marker)) {
                return at;
            }

            int next = Array.IndexOf(output, (byte)'\n', at);
            if (next < 0) {
                return -1;
            }

            at = next + 1;
        }

        return -1;
    }

    private static bool HasBinaryNote(byte[] output) {
        return output.AsSpan().IndexOf("Binary files "u8) >= 0;
    }
}
