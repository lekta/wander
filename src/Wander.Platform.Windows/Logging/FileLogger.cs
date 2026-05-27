using Wander.Core.Logging;

namespace Wander.Platform.Windows.Logging;

/// <summary>
/// Per-session file logger. On construction opens a fresh file named
/// <c>session-yyyyMMdd-HHmmss.log</c> under
/// <c>%LOCALAPPDATA%\Wander\logs\</c>. Writes are line-based, timestamped,
/// and synchronously flushed so a crash still leaves a useful tail.
/// </summary>
public sealed class FileLogger : ILogger, ILogFile, IDisposable {
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _disposed;


    public FileLogger() {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wander", "logs");
        Directory.CreateDirectory(folder);

        string fileName = $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        FilePath = Path.Combine(folder, fileName);
        _writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
    }


    public string FilePath { get; }


    public void Info(string message) => Write("INFO ", message, null);
    public void Warn(string message) => Write("WARN ", message, null);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);


    private void Write(string level, string message, Exception? ex) {
        if (_disposed) {
            return;
        }
        lock (_lock) {
            try {
                _writer.Write($"{DateTime.Now:HH:mm:ss.fff} {level} ");
                _writer.WriteLine(message);
                if (ex is not null) {
                    _writer.WriteLine(ex);
                }
            } catch {
                // A logger that throws would be a permanent UX outage; swallow.
            }
        }
    }


    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        try {
            _writer.Dispose();
        } catch {
            // ignore
        }
    }
}
