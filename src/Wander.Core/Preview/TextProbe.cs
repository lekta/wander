namespace Wander.Core.Preview;

/// <summary>
/// "Is this file text?", answered from its first few kilobytes.
///
/// <para>
/// Needed because some extensions mean either. A Unity <c>.asset</c> is
/// YAML in a project set to force text serialization and an opaque binary
/// blob in one that isn't — same name, same folder, nothing but the bytes
/// to tell them apart. Showing the binary one as text fills the pane with
/// mojibake, so the preview asks here first.
/// </para>
/// </summary>
public static class TextProbe {
    /// <summary>
    /// How much of the file to look at. A header is enough: formats that
    /// are binary are binary from the start, and reading more would mean
    /// paying for the whole file to answer a yes/no question.
    /// </summary>
    public const int SampleSize = 8192;

    /// <summary>
    /// Share of control characters above which the sample is called binary.
    /// Text does contain the odd stray control byte — a form feed, an
    /// escape sequence in a log — so the test is a proportion, not a veto.
    /// </summary>
    private const double ControlLimit = 0.05;


    /// <summary>
    /// True when <paramref name="sample"/> reads as text. An empty sample
    /// is text: an empty file has nothing to render, and the empty pane it
    /// produces says that better than "unsupported" does.
    /// </summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> sample) {
        if (sample.Length == 0) {
            return true;
        }

        // A byte-order mark settles it outright — nothing but text carries
        // one, and UTF-16 text is full of the NUL bytes the scan below
        // treats as proof of binary.
        if (HasTextBom(sample)) {
            return true;
        }

        int controls = 0;
        foreach (byte b in sample) {
            if (b == 0) {
                return false;
            }
            if (b < 0x20 && b is not ((byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0C or 0x1B)) {
                controls++;
            }
        }

        return controls <= sample.Length * ControlLimit;
    }


    private static bool HasTextBom(ReadOnlySpan<byte> sample) {
        return StartsWith(sample, 0xEF, 0xBB, 0xBF)      // UTF-8
            || StartsWith(sample, 0xFF, 0xFE)            // UTF-16 LE (and UTF-32 LE)
            || StartsWith(sample, 0xFE, 0xFF)            // UTF-16 BE
            || StartsWith(sample, 0x00, 0x00, 0xFE, 0xFF); // UTF-32 BE
    }

    private static bool StartsWith(ReadOnlySpan<byte> sample, params byte[] prefix) {
        return sample.Length >= prefix.Length && sample[..prefix.Length].SequenceEqual(prefix);
    }
}
