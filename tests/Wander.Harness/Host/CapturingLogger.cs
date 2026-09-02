using System.Diagnostics;
using Wander.Core.Logging;

namespace Wander.Harness.Host;

/// <summary>
/// Wraps the session logger: every line still goes to the file, and is
/// also kept in memory with a timestamp. The runner's "wait until quiet"
/// and its log assertions read from here; the report prints the tail.
/// </summary>
public sealed class CapturingLogger : ILogger, ILogFile {
    private readonly ILogger _inner;
    private readonly ILogFile _file;
    private readonly List<LogLine> _lines = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastLineMs;


    public CapturingLogger(ILogger inner, ILogFile file) {
        _inner = inner;
        _file = file;
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
        Keep("INFO", message);
    }

    public void Warn(string message) {
        _inner.Warn(message);
        Keep("WARN", message);
    }

    public void Error(string message, Exception? ex = null) {
        _inner.Error(message, ex);
        Keep("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");
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


    private void Keep(string level, string message) {
        var line = new LogLine(_clock.ElapsedMilliseconds, level, message);
        Interlocked.Exchange(ref _lastLineMs, line.AtMs);
        lock (_lines) {
            _lines.Add(line);
        }
        Logged?.Invoke(line);
    }
}


public sealed record LogLine(long AtMs, string Level, string Message);
