namespace Wander.Core.Logging;

/// <summary>
/// The session log for code that has no logger of its own: a view, a
/// window, a static helper. What is constructed with a logger keeps using
/// that one - the constructor is the list of what a class depends on.
///
/// <para>
/// Goes to the locator on every call, the way <c>Text</c> does: one lookup
/// under a lock is nothing next to writing a line to a file, and a cached
/// logger would miss the one the harness registers over the top after
/// startup (<c>HarnessApp</c>). Nothing registered - Core tests - and the
/// lines go nowhere (<see cref="NullLogger"/>).
/// </para>
///
/// <para>
/// Also what the log may say, from the debug settings (the view model
/// copies them here, <c>AppSettings.LogActions</c> and <c>LogPaths</c>):
/// <see cref="Details"/> - the trace of keys, clicks and selection;
/// <see cref="RevealPaths"/> - real paths instead of tokens
/// (<see cref="LogMask"/>). Both are off until the settings are read, so the
/// first lines of a session are masked whatever the setting says.
/// </para>
/// </summary>
public static class Log {
    private static volatile bool _details;
    private static volatile bool _revealPaths;


    /// <summary>The registered logger, or one that drops the lines - for what takes an <see cref="ILogger"/>.</summary>
    public static ILogger Current => ServiceLocator.TryGet<ILogger>() ?? NullLogger.Instance;

    /// <summary>Keys, clicks and changes of selection go into the log - see <see cref="Detail(string)"/>.</summary>
    public static bool Details {
        get => _details;
        set => _details = value;
    }

    /// <summary>Paths and names are written as they are, not as tokens.</summary>
    public static bool RevealPaths {
        get => _revealPaths;
        set => _revealPaths = value;
    }


    public static void Info(string message) => Current.Info(message);

    public static void Info(LogMessage message) => Current.Info(message.ToStringAndClear());

    public static void Warn(string message) => Current.Warn(message);

    public static void Warn(LogMessage message) => Current.Warn(message.ToStringAndClear());

    public static void Error(string message, Exception? ex = null) => Current.Error(message, ex);

    public static void Error(LogMessage message, Exception? ex = null) => Current.Error(message.ToStringAndClear(), ex);


    /// <summary>
    /// A line of the action trace - a key, a click, a selection that moved -
    /// written only with <see cref="Details"/> on. A session without it says
    /// what happened to the files; one with it says what the user did to get
    /// there, which is what "the selection slips every other time" needs.
    /// </summary>
    public static void Detail(string message) {
        if (Details) {
            Current.Info(message);
        }
    }

    /// <inheritdoc cref="Detail(string)"/>
    public static void Detail(DetailMessage message) {
        if (message.IsEnabled) {
            Current.Info(message.ToStringAndClear());
        }
    }


    /// <summary>
    /// A path or a bare name the way the log may show it: as it is with
    /// <see cref="RevealPaths"/>, a token otherwise. For names above all - a
    /// path in a <c>$"..."</c> line is masked by itself, while a name is a
    /// word like any other and has to be pointed out.
    /// </summary>
    public static string Path(string? value) {
        return RevealPaths ? value ?? "" : LogMask.Path(value);
    }
}
