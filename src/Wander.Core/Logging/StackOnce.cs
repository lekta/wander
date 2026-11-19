using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Wander.Core.Logging;

/// <summary>
/// The text a repeated failure goes into the log with: its stack the first
/// time a kind of fault is seen in a place, its message after that - a
/// search over ten thousand files that one extractor fails on wrote the same
/// twenty lines of stack ten thousand times (audit 2026-11-18, L12). An
/// expected failure - a COM error of a filter, a file held or out of reach -
/// is its message only: its stack tells nothing. Safe from many threads.
/// </summary>
public sealed class StackOnce {
    private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.Ordinal);


    /// <summary>What to write about <paramref name="ex"/> failing in <paramref name="where"/>.</summary>
    public string Describe(string where, Exception ex) {
        if (ex is COMException or IOException or UnauthorizedAccessException) {
            return ex.Message;
        }

        return _seen.TryAdd(RepeatCollapser.Signature("", where, ex), 0)
            ? ex.ToString()
            : $"{ex.GetType().Name}: {ex.Message} (stack written before)";
    }
}
