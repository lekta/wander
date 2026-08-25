using System.Text;

namespace Wander.Core.Companions;

/// <summary>Rating fields Wander reads out of a RawTherapee <c>.pp3</c>.</summary>
/// <param name="Rank">0…5 stars, null when the file doesn't say.</param>
/// <param name="ColorLabel">0…5 (0 = none), null when the file doesn't say.</param>
public sealed record Pp3Rating(int? Rank, int? ColorLabel);


/// <summary>
/// Reads and edits the two rating keys of a RawTherapee sidecar. A
/// <c>.pp3</c> is an INI file holding the user's entire develop recipe, so
/// this is written the paranoid way:
///
/// <list type="bullet">
///   <item>the file is handled as bytes — the original encoding and BOM
///   survive the round trip;</item>
///   <item>line endings are preserved per line, mixed endings included;</item>
///   <item>exactly one line changes, everything else is copied through
///   byte for byte. Losing a develop recipe would be losing the user's
///   work.</item>
/// </list>
///
/// <para>
/// Nothing here creates a <c>.pp3</c>: an empty sidecar changes how
/// RawTherapee treats the photo, so writing is only ever an edit of a file
/// that already exists.
/// </para>
/// </summary>
public static class Pp3Sidecar {
    private const string GeneralSection = "General";
    private const string RankKey = "Rank";
    private const string ColorLabelKey = "ColorLabel";

    /// <summary>Highest star count RawTherapee's browser shows.</summary>
    public const int MaxRank = 5;


    public static Pp3Rating Read(byte[] content) {
        string text = Decode(content, out _, out _);
        int? rank = null;
        int? color = null;

        foreach (var (section, key, value) in Entries(text)) {
            if (!string.Equals(section, GeneralSection, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (rank is null && string.Equals(key, RankKey, StringComparison.OrdinalIgnoreCase)) {
                rank = ParseInt(value);
            } else if (color is null && string.Equals(key, ColorLabelKey, StringComparison.OrdinalIgnoreCase)) {
                color = ParseInt(value);
            }
        }

        return new Pp3Rating(rank, color);
    }


    /// <summary>
    /// The same file with <c>[General] Rank</c> set to <paramref name="rank"/>.
    /// The key is rewritten in place when present; otherwise it is inserted
    /// at the top of the <c>[General]</c> section, and that section is
    /// appended if the file lacks one.
    /// </summary>
    public static byte[] WithRank(byte[] content, int rank) {
        if (rank < 0 || rank > MaxRank) {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, $"Rank must be 0..{MaxRank}.");
        }

        string text = Decode(content, out var encoding, out bool hasBom);
        string updated = SetKey(text, RankKey, rank.ToString());

        return Encode(updated, encoding, hasBom);
    }


    // --- Text plumbing -------------------------------------------------
    // Lines are split on '\n' only and the '\r' stays glued to the end of
    // each piece, so re-joining reproduces the original endings exactly —
    // including a file that mixes them.

    private static string SetKey(string text, string key, string value) {
        var lines = text.Split('\n');
        string? section = null;
        int generalStart = -1;

        for (int i = 0; i < lines.Length; i++) {
            string body = lines[i].TrimEnd('\r');
            string trimmed = body.Trim();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) {
                section = trimmed[1..^1].Trim();
                if (generalStart < 0 && string.Equals(section, GeneralSection, StringComparison.OrdinalIgnoreCase)) {
                    generalStart = i;
                }
                continue;
            }

            if (!string.Equals(section, GeneralSection, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            int eq = body.IndexOf('=');
            if (eq < 0 || !string.Equals(body[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            string eol = lines[i].EndsWith('\r') ? "\r" : "";
            lines[i] = $"{key}={value}{eol}";

            return string.Join('\n', lines);
        }

        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (generalStart >= 0) {
            var withKey = new List<string>(lines);
            withKey.Insert(generalStart + 1, $"{key}={value}{(newline == "\r\n" ? "\r" : "")}");

            return string.Join('\n', withKey);
        }

        string tail = text.Length == 0 || text.EndsWith('\n') ? "" : newline;

        return $"{text}{tail}[{GeneralSection}]{newline}{key}={value}{newline}";
    }

    private static IEnumerable<(string Section, string Key, string Value)> Entries(string text) {
        string section = "";
        foreach (string raw in text.Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']')) {
                section = line[1..^1].Trim();
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq > 0) {
                yield return (section, line[..eq].Trim(), line[(eq + 1)..].Trim());
            }
        }
    }

    private static int? ParseInt(string value) {
        return int.TryParse(value, out int parsed) ? parsed : null;
    }


    // --- Encoding ------------------------------------------------------

    private static string Decode(byte[] content, out Encoding encoding, out bool hasBom) {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) {
            encoding = new UTF8Encoding(true);
            hasBom = true;

            return new UTF8Encoding(false).GetString(content, 3, content.Length - 3);
        }
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE) {
            encoding = new UnicodeEncoding(false, true);
            hasBom = true;

            return new UnicodeEncoding(false, false).GetString(content, 2, content.Length - 2);
        }
        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF) {
            encoding = new UnicodeEncoding(true, true);
            hasBom = true;

            return new UnicodeEncoding(true, false).GetString(content, 2, content.Length - 2);
        }

        // RawTherapee writes plain UTF-8 without a BOM.
        encoding = new UTF8Encoding(false);
        hasBom = false;

        return encoding.GetString(content);
    }

    private static byte[] Encode(string text, Encoding encoding, bool hasBom) {
        byte[] body = encoding.GetBytes(text);
        if (!hasBom) {
            return body;
        }

        byte[] preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);

        return result;
    }
}
