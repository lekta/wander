using System.Diagnostics;
using System.Text;
using Wander.Core.Actions;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// <c>git</c>, found where the actions find their tools
/// (<see cref="IToolLocator"/>), started without a window. stdout is kept
/// as bytes up to the limit and the program is stopped there: the
/// differences of two big files can run to more than anyone reads.
///
/// <para>
/// Not through <see cref="WindowsProcessRunner"/>: that one drains stdout
/// without keeping it. The arguments go in one by one
/// (<see cref="ProcessStartInfo.ArgumentList"/>) - a path is never quoted
/// by hand. The working folder is the temp one, so a repository the files
/// happen to sit in brings no config of its own.
/// </para>
/// </summary>
public sealed class GitDiffTool : IDiffTool {
    /// <summary>How much of stderr is kept - the last lines say what went wrong.</summary>
    private const int ErrorChars = 1000;

    private readonly IToolLocator _locator;


    public GitDiffTool(IToolLocator locator) {
        _locator = locator;
    }


    public bool IsAvailable => _locator.Find("git") is not null;


    public async Task<DiffToolRun> RunAsync(IReadOnlyList<string> arguments, int maxOutput, CancellationToken ct) {
        string git = _locator.Find("git") ?? throw new InvalidOperationException("git is not installed.");
        var info = new ProcessStartInfo {
            FileName = git,
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments) {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        try {
            var error = process.StandardError.ReadToEndAsync(ct);
            var (output, cut) = await ReadUpToAsync(process.StandardOutput.BaseStream, maxOutput, ct).ConfigureAwait(false);
            if (cut) {
                Stop(process);
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            string tail = await error.ConfigureAwait(false);
            if (tail.Length > ErrorChars) {
                tail = tail[^ErrorChars..];
            }

            return new DiffToolRun(cut ? -1 : process.ExitCode, output, cut, tail.Trim());
        } catch (OperationCanceledException) {
            Stop(process);

            throw;
        }
    }


    /// <summary>Up to <paramref name="limit"/> bytes, and whether there was more.</summary>
    private static async Task<(byte[] Bytes, bool Cut)> ReadUpToAsync(Stream stream, int limit, CancellationToken ct) {
        var kept = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true) {
            int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) {
                return (kept.ToArray(), false);
            }

            int room = limit - (int)kept.Length;
            if (read > room) {
                kept.Write(buffer, 0, room);

                return (kept.ToArray(), true);
            }

            kept.Write(buffer, 0, read);
        }
    }

    private static void Stop(Process process) {
        try {
            process.Kill(entireProcessTree: true);
        } catch (InvalidOperationException) {
            // Exited on its own in the meantime.
        }
    }
}
