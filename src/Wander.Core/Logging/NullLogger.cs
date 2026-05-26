namespace Wander.Core.Logging;

/// <summary>
/// Drop-in no-op logger. Used in tests and as the default if no file logger
/// has been registered (so core code can always assume <see cref="ILogger"/>
/// is available without null-checks).
/// </summary>
public sealed class NullLogger : ILogger {
    public static readonly NullLogger Instance = new();

    private NullLogger() { }

    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? ex = null) { }
}
