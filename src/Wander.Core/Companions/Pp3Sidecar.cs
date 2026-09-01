using System.Text;
using Wander.Core.FileSystem;

namespace Wander.Core.Companions;

/// <summary>
/// Reads and edits the two rating keys of a RawTherapee sidecar. A
/// <c>.pp3</c> is an INI file holding the user's entire develop recipe, so
/// this is written the paranoid way: exactly one line changes and every
/// other byte is copied through. Losing a develop recipe would be losing
/// the user's work.
///
/// <para>
/// Encoding, BOM and line endings are <see cref="SidecarText"/>'s job.
/// </para>
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


    /// <summary>
    /// A brand-new <c>.pp3</c> holding nothing but the rating fields.
    ///
    /// <para>
    /// Read the warning on <see cref="SidecarFormat.Pp3"/> before calling
    /// this: RawTherapee applies its default processing profile only to
    /// photos with no sidecar, so bringing one into existence — even one
    /// this empty — is a statement about how the photo develops, not only
    /// about how many stars it has. Nothing here decides that a file may be
    /// created; <see cref="CompanionMetadataService.CreateRatingSidecar"/>
    /// asks first.
    /// </para>
    ///
    /// <para>
    /// No <c>[Version]</c> section: the only honest value would be the
    /// version of a RawTherapee we are not, and a wrong one sends RT down
    /// its compatibility paths for a file that has nothing to be
    /// compatible about.
    /// </para>
    /// </summary>
    public static byte[] Create(int rank, int colorLabel) {
        Guard(rank, MaxRank, nameof(rank));
        Guard(colorLabel, ColorLabels.Max, nameof(colorLabel));

        string text = $"[{GeneralSection}]\r\n{RankKey}={rank}\r\n{ColorLabelKey}={colorLabel}\r\n";

        return SidecarText.Encode(text, new UTF8Encoding(false), hasBom: false);
    }


    public static SidecarRating Read(byte[] content) {
        string text = SidecarText.Decode(content, out _, out _);
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

        return new SidecarRating(rank, color, color is int c ? ColorLabels.Name(c) : null);
    }


    /// <summary>The same file with <c>[General] Rank</c> set to <paramref name="rank"/>.</summary>
    public static byte[] WithRank(byte[] content, int rank) {
        Guard(rank, MaxRank, nameof(rank));

        return SidecarText.Edit(content, text => SetKey(text, RankKey, rank.ToString()));
    }


    /// <summary>The same file with <c>[General] ColorLabel</c> set to <paramref name="label"/>.</summary>
    public static byte[] WithColorLabel(byte[] content, int label) {
        Guard(label, ColorLabels.Max, nameof(label));

        return SidecarText.Edit(content, text => SetKey(text, ColorLabelKey, label.ToString()));
    }


    private static void Guard(int value, int max, string name) {
        if (value < 0 || value > max) {
            throw new ArgumentOutOfRangeException(name, value, $"Value must be 0..{max}.");
        }
    }


    // --- INI plumbing --------------------------------------------------

    /// <summary>
    /// Rewrites one key in place when it's there, inserts it at the top of
    /// <c>[General]</c> when it isn't, and appends that section if the file
    /// has none. Everything else is untouched, including comments, blank
    /// lines and key order.
    /// </summary>
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

        string newline = SidecarText.NewlineOf(text);
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
}
