namespace Wander.Core.Logging;

/// <summary>
/// Minimal logging contract. Real implementation writes per-session files in
/// %LOCALAPPDATA%\Wander\logs\; tests use <see cref="NullLogger"/> so they
/// don't pollute the disk.
///
/// <para>
/// A line written as <c>$"..."</c> goes in through the
/// <see cref="LogMessage"/> overloads: the paths in its values are masked
/// on the way, unless real paths are on (<see cref="Log.RevealPaths"/>).
/// An implementation only ever writes finished strings.
/// </para>
/// </summary>
public interface ILogger {
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);

    void Info(LogMessage message) => Info(message.ToStringAndClear());
    void Warn(LogMessage message) => Warn(message.ToStringAndClear());
    void Error(LogMessage message, Exception? ex = null) => Error(message.ToStringAndClear(), ex);
}
