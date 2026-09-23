using System.Runtime.CompilerServices;

namespace Wander.Core.Logging;

/// <summary>
/// A log line written as <c>$"..."</c>: every text value put into it goes
/// through <see cref="LogMask.Scrub"/> on the way in, unless real paths are
/// on (<see cref="Log.RevealPaths"/>). One value at a time is the point - a
/// path is exactly the value it was, so where it ends is known, and the
/// words of the line around it stay as written. Numbers, counts and enums
/// go in as they are.
///
/// <para>
/// A bare name is not a path and nothing tells it from any other word:
/// it goes in through <see cref="Log.Path"/>. The line as a whole is
/// masked once more by the file logger, for what reaches it as a finished
/// string.
/// </para>
/// </summary>
[InterpolatedStringHandler]
public ref struct LogMessage {
    private DefaultInterpolatedStringHandler _text;


    public LogMessage(int literalLength, int formattedCount) {
        _text = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
    }


    public void AppendLiteral(string value) {
        _text.AppendLiteral(value);
    }

    public void AppendFormatted(string? value) {
        _text.AppendLiteral(Log.RevealPaths ? value ?? "" : LogMask.Scrub(value));
    }

    public void AppendFormatted(string? value, int alignment) {
        _text.AppendFormatted(Log.RevealPaths ? value : LogMask.Scrub(value), alignment);
    }

    public void AppendFormatted<T>(T value) {
        if (value is string text) {
            AppendFormatted(text);

            return;
        }

        _text.AppendFormatted(value);
    }

    public void AppendFormatted<T>(T value, string? format) {
        _text.AppendFormatted(value, format);
    }

    public void AppendFormatted<T>(T value, int alignment) {
        _text.AppendFormatted(value, alignment);
    }

    public void AppendFormatted<T>(T value, int alignment, string? format) {
        _text.AppendFormatted(value, alignment, format);
    }

    /// <summary>The finished line; the handler is spent after this.</summary>
    public string ToStringAndClear() {
        return _text.ToStringAndClear();
    }
}
