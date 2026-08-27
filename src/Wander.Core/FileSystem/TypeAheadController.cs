namespace Wander.Core.FileSystem;

/// <summary>
/// "Type a few letters and land on the file" — the jump-to-name behaviour
/// every file manager has. Keystrokes arriving close together build up a
/// prefix; a pause drops it, so the next letter starts a new search rather
/// than continuing one from a minute ago.
///
/// <para>
/// Pure and clock-injectable so the whole thing is testable: it never looks
/// at the list containers, only at names and at the index currently
/// selected, and answers with the index to move to.
/// </para>
///
/// <para>
/// Two rules make this feel like Windows rather than like a search box. The
/// search wraps around the end of the list, and where it starts depends on
/// how much has been typed: a single letter looks past the current item (so
/// pressing it again walks through everything starting with it), while a
/// longer prefix starts at the current item (so refining "b" to "be" narrows
/// down what is already selected instead of snapping back to the top of the
/// folder).
/// </para>
/// </summary>
public sealed class TypeAheadController {
    /// <summary>
    /// How long a prefix survives without new input. A second is what
    /// Windows itself uses; long enough to type a word, short enough that a
    /// letter typed after a glance at the screen starts fresh.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    private readonly Func<DateTime> _now;
    private readonly TimeSpan _timeout;

    private string _prefix = "";
    private DateTime _lastInput = DateTime.MinValue;


    public TypeAheadController(Func<DateTime>? now = null, TimeSpan? timeout = null) {
        _now = now ?? (() => DateTime.UtcNow);
        _timeout = timeout ?? DefaultTimeout;
    }


    /// <summary>What has been typed so far, for tests and diagnostics.</summary>
    public string Prefix => _prefix;


    /// <summary>
    /// Feeds one keystroke and says where to go.
    /// </summary>
    /// <param name="text">The character typed. Anything else is ignored.</param>
    /// <param name="names">Item names, in the order they appear on screen.</param>
    /// <param name="currentIndex">Index of the selected item, or -1 when nothing is selected.</param>
    /// <returns>Index to select, or -1 when nothing matches.</returns>
    public int Type(string text, IReadOnlyList<string> names, int currentIndex) {
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]) || names.Count == 0) {
            return -1;
        }

        var now = _now();
        bool expired = now - _lastInput > _timeout;
        _lastInput = now;

        if (expired) {
            _prefix = "";
        }

        // Same single letter again: step to the next item starting with it,
        // rather than looking for a file whose name begins "aa".
        bool repeat = text.Length == 1 && _prefix.Length > 0 && IsAll(_prefix, text[0]);
        _prefix = repeat ? text : _prefix + text;

        // A single letter always moves on: the item under the cursor is the
        // one the user is looking away from. A growing prefix starts where
        // the cursor is, so refining "b" to "be" keeps narrowing down what is
        // already selected instead of jumping back to the top of the folder.
        int start = _prefix.Length == 1 ? currentIndex + 1 : Math.Max(0, currentIndex);

        return FindFrom(names, _prefix, start);
    }


    /// <summary>
    /// Forgets what was typed. Called when the list is rebuilt or the user
    /// moves away — a prefix left over from the previous folder would make
    /// the next letter jump somewhere unexplainable.
    /// </summary>
    public void Reset() {
        _prefix = "";
        _lastInput = DateTime.MinValue;
    }


    private static int FindFrom(IReadOnlyList<string> names, string prefix, int start) {
        for (int i = 0; i < names.Count; i++) {
            int index = (start + i) % names.Count;
            if (names[index].StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)) {
                return index;
            }
        }

        return -1;
    }

    private static bool IsAll(string s, char c) {
        foreach (char ch in s) {
            if (char.ToUpperInvariant(ch) != char.ToUpperInvariant(c)) {
                return false;
            }
        }

        return true;
    }
}
