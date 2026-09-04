using Wander.Core.Logging;
using Wander.Core.Persistence;

namespace Wander.Platform.Windows.Logging;

/// <summary>
/// Per-session file logger. On construction opens a fresh file named
/// <c>session-yyyyMMdd-HHmmss.log</c> under <see cref="AppPaths.Logs"/>
/// (<c>%LOCALAPPDATA%\Wander\logs\</c> by default). Writes are line-based, timestamped,
/// and synchronously flushed so a crash still leaves a useful tail.
/// </summary>
public sealed class FileLogger : ILogger, ILogFile, IDisposable {
    private readonly StreamWriter _writer;
    private readonly RepeatCollapser _collapser = new();
    private readonly object _lock = new();

    // The line a run of repeats is about: its level and its message, for
    // the summary that closes the run.
    private string _runLevel = "INFO";
    private string _runMessage = "";
    private bool _disposed;


    public FileLogger() {
        // Never throw: the logger is constructed first in the bootstrapper,
        // and a failure here (locked file, read-only profile, two instances
        // started in the same second) must not prevent app startup. PID in
        // the name keeps concurrent instances from fighting over one file.
        try {
            string folder = AppPaths.Logs;
            Directory.CreateDirectory(folder);

            string fileName = $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log";
            FilePath = Path.Combine(folder, fileName);
            _writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
        } catch {
            FilePath = "";
            _writer = StreamWriter.Null;
        }
    }


    /// <summary>
    /// Every line written, whichever thread wrote it: level, message,
    /// exception. Exists for the test harness, and for a reason no wrapper
    /// can cover - the services the bootstrapper builds are handed this
    /// logger and keep it, so a logger registered over the top afterwards
    /// never sees a word from them, and "the run logged no errors" would be
    /// a statement about half the application.
    /// </summary>
    /// <remarks>
    /// Raised while the write lock is held, so subscribers see the lines in
    /// the order they were written - which means a subscriber must not log,
    /// and must not block.
    /// </remarks>
    public event Action<string, string, Exception?>? Written;


    public string FilePath { get; }


    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);


    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        try {
            // The tail of a flood belongs in the file, not in the state of
            // an object about to go away.
            lock (_lock) {
                if (_collapser.Flush() is { } repeats) {
                    EmitSummary(repeats);
                }
            }
            _writer.Dispose();
        } catch {
            // ignore
        }
    }


    private void Write(string level, string message, Exception? ex) {
        if (_disposed) {
            return;
        }
        lock (_lock) {
            // Only warnings and errors are collapsed. An INFO line is the
            // chronology itself, and two identical ones a second apart are
            // two events, so they go straight through - and a run of
            // repeats in progress is left as it is: its summary names the
            // message it is about, so an INFO line in between does not
            // confuse it.
            if (level is not ("WARN" or "ERROR")) {
                Emit(level, message, ex);
            } else {
                // Asked under the lock, because the answer is about the
                // line written just before this one and two threads must
                // not both think they are that line.
                var decision = _collapser.Decide(RepeatCollapser.Signature(level, message, ex), DateTime.UtcNow);
                if (decision.Repeats is { } repeats) {
                    // The summary belongs to the run that is ending, so it
                    // goes out about that run's line, before it moves on.
                    EmitSummary(repeats);
                }
                if (decision.Write) {
                    _runLevel = level;
                    _runMessage = message;
                    Emit(level, message, ex);
                }
            }

            try {
                // Every call, written or collapsed: the harness counts
                // errors through this event, and a flood that is being
                // collapsed in the file is still a flood of errors.
                Written?.Invoke(level, message, ex);
            } catch {
                // Same rule as in Emit: nothing a listener does may reach
                // the caller, which is somewhere in the middle of a file
                // copy.
            }
        }
    }

    /// <summary>
    /// The line that stands in for a run of repeats, at the run's level and
    /// naming its message: "ERROR repeated 16277 times over 271 s: Unhandled
    /// dispatcher exception".
    /// </summary>
    private void EmitSummary(RepeatCollapser.Summary repeats) {
        Emit(_runLevel, $"{repeats.Line}: {_runMessage}", null);
    }

    private void Emit(string level, string message, Exception? ex) {
        try {
            _writer.Write($"{DateTime.Now:HH:mm:ss.fff} {level,-5} ");
            _writer.WriteLine(message);
            if (ex is not null) {
                _writer.WriteLine(ex);
            }
        } catch {
            // A logger that throws would be a permanent UX outage; swallow.
        }
    }
}
