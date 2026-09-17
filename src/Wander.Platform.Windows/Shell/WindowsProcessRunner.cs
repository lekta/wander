using System.Diagnostics;
using System.Text;
using Wander.Core.Actions;
using Wander.Core.Logging;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Starts a program with <see cref="Process"/> and waits for it, killing
/// the whole tree when the token says so. stderr is read only when the
/// program runs without a window of its own: redirecting the stream of a
/// program the user can see would take its error messages away from the
/// console they are looking at.
///
/// <para>
/// Both pipes are drained while the process runs, not after: a program
/// that fills a pipe nobody reads blocks on its next write and never
/// exits, and stdout is drained for exactly that reason even though nothing
/// is kept of it.
/// </para>
///
/// <para>
/// Every program between its start and its exit is on a list, so an exit
/// that cannot wait for the cancellations to arrive still reaches them all
/// (<see cref="KillAll"/>). Static: the list is the process's, not one
/// runner's.
/// </para>
/// </summary>
public sealed class WindowsProcessRunner : IProcessRunner {
    /// <summary>How much of stderr is kept - the last lines say what went wrong.</summary>
    private const int TailChars = 2000;

    private static readonly List<Process> _running = new();

    private readonly ILogger _log;


    public WindowsProcessRunner(ILogger log) {
        _log = log;
    }


    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct) {
        var info = new ProcessStartInfo {
            FileName = request.Program,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = request.HideWindow,
            RedirectStandardError = request.HideWindow,
            RedirectStandardOutput = request.HideWindow,
        };

        var watch = Stopwatch.StartNew();
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start {request.Program}.");
        lock (_running) {
            _running.Add(process);
        }

        try {
            var tail = new StringBuilder();
            var stderr = request.HideWindow ? DrainAsync(process.StandardError, tail) : Task.CompletedTask;
            var stdout = request.HideWindow ? DrainAsync(process.StandardOutput, null) : Task.CompletedTask;

            bool killed = false;
            try {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                killed = true;
                try {
                    process.Kill(entireProcessTree: true);
                } catch (InvalidOperationException) {
                    // Exited between the cancellation and the kill.
                }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await Task.WhenAll(stderr, stdout).ConfigureAwait(false);

            return new ProcessResult(process.ExitCode, tail.ToString(), watch.Elapsed, killed);
        } finally {
            lock (_running) {
                _running.Remove(process);
            }
        }
    }

    public void KillAll() {
        Process[] running;
        lock (_running) {
            running = _running.ToArray();
        }
        if (running.Length == 0) {
            return;
        }

        _log.Info($"Killing {running.Length} running program(s)");
        foreach (var process in running) {
            try {
                process.Kill(entireProcessTree: true);
            } catch (Exception ex) {
                // Exited or disposed since the list was read, or refused:
                // either way there is nothing more this can do.
                _log.Warn($"Kill failed: {ex.Message}");
            }
        }
    }


    /// <summary>Reads a stream to its end, keeping the last <see cref="TailChars"/> when asked to keep anything.</summary>
    private static async Task DrainAsync(StreamReader reader, StringBuilder? keep) {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0) {
            if (keep is null) {
                continue;
            }
            keep.Append(buffer, 0, read);
            if (keep.Length > TailChars * 2) {
                keep.Remove(0, keep.Length - TailChars);
            }
        }

        if (keep is not null && keep.Length > TailChars) {
            keep.Remove(0, keep.Length - TailChars);
        }
    }
}
