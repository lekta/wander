using System.Globalization;
using System.Text.RegularExpressions;

namespace Wander.Core.FileSystem;

/// <summary>
/// The one rule for a name that is already taken: a number in brackets
/// before the extension, the one <em>after the highest</em> already there.
/// "clip (3)" and "clip (4)" are followed by "clip (5)", not by a "clip (1)"
/// filling a gap - the newest result is then the last one in the list,
/// and a number never comes back to mean a different file. Explorer,
/// Finder and browsers fill the first gap instead (decision of 2026-09-16,
/// ARCHITECTURE "Файловые операции"); every place that invents a name -
/// a copy kept beside its original, an extraction, an action's output -
/// comes here, so the two never disagree.
/// </summary>
public static class UniqueNames {
    /// <param name="desiredPath">The path wanted; returned as it is when nothing has it.</param>
    /// <param name="exists">Whether a path is taken, by a file or a folder.</param>
    /// <param name="namesIn">The names in a folder; asked only when the plain name is taken.</param>
    public static string Resolve(string desiredPath, Func<string, bool> exists, Func<string, IEnumerable<string>> namesIn) {
        if (!exists(desiredPath)) {
            return desiredPath;
        }

        string dir = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(desiredPath);
        string extension = Path.GetExtension(desiredPath);
        var numbered = new Regex(
            "^" + Regex.Escape(stem) + @" \((\d{1,9})\)" + Regex.Escape(extension) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        int highest = 0;
        foreach (string sibling in namesIn(dir)) {
            var match = numbered.Match(sibling);
            if (match.Success) {
                highest = Math.Max(highest, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        // The listing is a moment old by now; the check still has the last word.
        for (int i = highest + 1; ; i++) {
            string candidate = Path.Combine(dir, $"{stem} ({i}){extension}");
            if (!exists(candidate)) {
                return candidate;
            }
        }
    }
}
