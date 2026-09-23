using System.Runtime.CompilerServices;

namespace Wander.Core.Logging;

/// <summary>
/// A line of the action trace written as <c>$"..."</c> - a
/// <see cref="LogMessage"/> that is not even put together while the trace
/// is off (<see cref="Log.Details"/>): the compiler asks first and skips
/// the values, so a key press or a selection change costs one flag read.
/// </summary>
[InterpolatedStringHandler]
public ref struct DetailMessage {
    private LogMessage _line;


    public DetailMessage(int literalLength, int formattedCount, out bool enabled) {
        enabled = Log.Details;
        IsEnabled = enabled;
        _line = enabled ? new LogMessage(literalLength, formattedCount) : default;
    }


    /// <summary>Whether the trace was on when the line was begun - and so whether there is a line.</summary>
    public bool IsEnabled { get; }


    public void AppendLiteral(string value) {
        _line.AppendLiteral(value);
    }

    public void AppendFormatted(string? value) {
        _line.AppendFormatted(value);
    }

    public void AppendFormatted<T>(T value) {
        _line.AppendFormatted(value);
    }

    public void AppendFormatted<T>(T value, string? format) {
        _line.AppendFormatted(value, format);
    }

    /// <summary>The finished line; the handler is spent after this.</summary>
    public string ToStringAndClear() {
        return _line.ToStringAndClear();
    }
}
