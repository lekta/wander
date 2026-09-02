using System.Diagnostics;
using Wander.Core.Logging;
using Wander.Platform.Windows.Logging;

namespace Wander.Harness.Host;

/// <summary>
/// Wraps the session logger: every line still goes to the file, and is
/// also kept in memory with a timestamp. The runner's "wait until quiet"
/// and its log assertions read from here; the report prints the tail.
///
/// <para>
/// Lines are taken from the file logger's own <c>Written</c> event rather
/// than from the calls that come through this wrapper, and that is not a
/// detail: the services the bootstrapper builds - file operations, the
/// shell, the watcher - are handed the logger at construction and never
/// look it up again, so a wrapper registered afterwards sees nothing they
/// write. Listening at the source is what makes <c>assert-log noErrors</c>
/// a statement about the whole application instead of about the view
/// model.
/// </para>
/// </summary>
public sealed class CapturingLogger : ILogger, ILogFile {
    private readonly ILogger _inner;
    private readonly ILogFile _file;
    private readonly List<LogLine> _lines = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _keepOwnCalls;
    private long _lastLineMs;


    public CapturingLogger(ILogger inner, ILogFile file) {
        _inner = inner;
        _file = file;
        if (inner is FileLogger source) {
            source.Written += Keep;
        } else {
            // No event to listen to - a fake logger in a unit test. Keep
            // what comes through the wrapper, which is all there is.
            _keepOwnCalls = true;
        }
    }


    public string FilePath => _file.FilePath;

    /// <summary>How long ago anything was logged - the runner's idea of "quiet".</summary>
    public long MillisecondsSinceLastLine => _clock.ElapsedMilliseconds - Interlocked.Read(ref _lastLineMs);

    public int Count {
        get {
            lock (_lines) {
                return _lines.Count;
            }
        }
    }

    /// <summary>Raised on the logging thread, whichever it is.</summary>
    public event Action<LogLine>? Logged;


    public void Info(string message) {
        _inner.Info(message);
        KeepOwn("INFO", message, null);
    }

    public void Warn(string message) {
        _inner.Warn(message);
        KeepOwn("WARN", message, null);
    }

    public void Error(string message, Exception? ex = null) {
        _inner.Error(message, ex);
        KeepOwn("ERROR", message, ex);
    }

    /// <summary>Lines from <paramref name="fromIndex"/> on - a step asserts against what it caused, not the whole session.</summary>
    public IReadOnlyList<LogLine> Since(int fromIndex) {
        lock (_lines) {
            return _lines.Skip(fromIndex).ToList();
        }
    }

    public IReadOnlyList<LogLine> All() {
        return Since(0);
    }


    private void KeepOwn(string level, string message, Exception? ex) {
        if (_keepOwnCalls) {
            Keep(level, message, ex);
        }
    }

    private void Keep(string level, string message, Exception? ex) {
        var line = new LogLine(
            _clock.ElapsedMilliseconds,
            level,
            ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");
        Interlocked.Exchange(ref _lastLineMs, line.AtMs);
        lock (_lines) {
            _lines.Add(line);
        }
        Logged?.Invoke(line);
    }
}


public sealed record LogLine(long AtMs, string Level, string Message);
