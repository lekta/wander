namespace Wander.Core.Logging;

/// <summary>
/// Optional sibling of <see cref="ILogger"/> exposed by file-backed loggers.
/// Lives in its own interface so the contract for <see cref="ILogger"/> stays
/// transport-agnostic (a future console or remote logger wouldn't have a file).
/// </summary>
public interface ILogFile {
    /// <summary>Absolute path of the current session's log file.</summary>
    string FilePath { get; }
}
