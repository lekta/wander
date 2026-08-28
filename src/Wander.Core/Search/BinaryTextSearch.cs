using System.Text;

namespace Wander.Core.Search;

/// <summary>
/// "Does this byte sequence occur in this file", for files that are not
/// text and never will be — executables, resource blobs, save games.
///
/// <para>
/// Off by default and behind its own switch, which is what every tool that
/// offers it does: <c>grep</c> and <c>ripgrep</c> skip binaries until
/// <c>-a</c>, VS Code skips them silently, Windows Search only ever sees
/// what a filter hands it. The reason is not cost but noise — a query for
/// a common word matches something inside almost every large binary, and a
/// result list of DLLs is not an answer.
/// </para>
///
/// <para>
/// ASCII only, deliberately. In a file we have already decided is binary
/// there is nothing to decode: no byte order mark, no consistent codepage,
/// often several encodings in one file. Guessing one would produce matches
/// that cannot be explained and misses that cannot be diagnosed, so the
/// rule is the narrow one that is always true — the bytes of the query as
/// written. A non-ASCII query therefore matches nothing here, and
/// <see cref="Supports"/> says so up front instead of failing quietly.
/// </para>
/// </summary>
public static class BinaryTextSearch {
    /// <summary>
    /// Whether a raw byte search can be run for this query at all. False
    /// for anything with a character above <c>0x7F</c> — Cyrillic, accents,
    /// emoji. Callers tell the user rather than returning "not found".
    /// </summary>
    public static bool Supports(string query) {
        if (string.IsNullOrEmpty(query)) {
            return false;
        }

        foreach (char c in query) {
            if (c > 0x7F) {
                return false;
            }
        }

        return true;
    }


    /// <summary>
    /// True when the query's ASCII bytes occur in the buffer. Case is
    /// folded for the letters, so a query matches a name stored in either
    /// case — everything else compares byte for byte.
    /// </summary>
    public static bool Contains(ReadOnlySpan<byte> bytes, string query) {
        if (!Supports(query) || bytes.Length < query.Length) {
            return false;
        }

        var needle = Encoding.ASCII.GetBytes(query);
        for (int i = 0; i < needle.Length; i++) {
            needle[i] = Fold(needle[i]);
        }

        int last = bytes.Length - needle.Length;
        for (int start = 0; start <= last; start++) {
            int i = 0;
            while (i < needle.Length && Fold(bytes[start + i]) == needle[i]) {
                i++;
            }
            if (i == needle.Length) {
                return true;
            }
        }

        return false;
    }


    /// <summary>ASCII upper-casing. Only the letters move; every other byte is itself.</summary>
    private static byte Fold(byte b) {
        return b is >= (byte)'a' and <= (byte)'z' ? (byte)(b - 32) : b;
    }
}
