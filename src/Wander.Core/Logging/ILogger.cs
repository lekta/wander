namespace Wander.Core.Logging;

/// <summary>
/// Minimal logging contract. Real implementation writes per-session files in
/// %LOCALAPPDATA%\Wander\logs\; tests use <see cref="NullLogger"/> so they
/// don't pollute the disk.
/// </summary>
public interface ILogger {
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
