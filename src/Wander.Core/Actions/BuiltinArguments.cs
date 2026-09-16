using System.Globalization;

namespace Wander.Core.Actions;

/// <summary>
/// A built-in action's own settings, as <see cref="CustomAction.Arguments"/>
/// stores them: <c>key=value</c> pairs separated by <c>;</c>. Keys are
/// case-insensitive, spaces around either side are dropped, a later key
/// wins, and a fragment without <c>=</c> or without a key is ignored -
/// the row is typed by hand, and a stray <c>;</c> must not make it fail.
/// </summary>
public sealed class BuiltinArguments {
    private readonly Dictionary<string, string> _values;


    private BuiltinArguments(Dictionary<string, string> values) {
        _values = values;
    }


    public static BuiltinArguments Parse(string text) {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            int eq = part.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            string key = part[..eq].Trim();
            if (key.Length > 0) {
                values[key] = part[(eq + 1)..].Trim();
            }
        }

        return new BuiltinArguments(values);
    }


    /// <summary>The value under <paramref name="key"/>, or null when it is not there.</summary>
    public string? Get(string key) {
        return _values.TryGetValue(key, out string? value) ? value : null;
    }

    /// <summary>The whole number under <paramref name="key"/>, clamped; <paramref name="fallback"/> when absent or not a number.</summary>
    public int GetInt(string key, int fallback, int min, int max) {
        return Get(key) is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }
}
