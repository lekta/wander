using System.Globalization;
using System.Text.RegularExpressions;
using Wander.Core.Companions;
using Wander.Core.FileSystem;

namespace Wander.Core.Rename;

/// <summary>
/// One file or folder the batch-rename window was given.
/// </summary>
/// <param name="FullPath">Where it is now; the name is taken from it.</param>
/// <param name="IsFolder">A folder has no extension: its whole name is <c>[N]</c>.</param>
/// <param name="ModifiedUtc">What <c>[D]</c> prints.</param>
/// <param name="Companions">Sidecars that follow the file, as the listing knows them.</param>
public sealed record RenameItem(
    string FullPath, bool IsFolder, DateTime ModifiedUtc, IReadOnlyList<string>? Companions = null) {

    public string Name => Path.GetFileName(FullPath);


    public static RenameItem From(FileSystemEntry entry) {
        return new RenameItem(entry.FullPath, entry.Kind == EntryKind.Directory, entry.ModifiedUtc, entry.Companions);
    }
}


public enum RenameRowStatus {
    /// <summary>The rules leave this name as it is; the row is skipped.</summary>
    Unchanged,
    Renamed,

    /// <summary>Empty, a reserved device name, forbidden characters, a trailing dot or space.</summary>
    InvalidName,

    /// <summary>Two rows of the batch would end up with the same name.</summary>
    DuplicateInBatch,

    /// <summary>A file outside the batch already has this name.</summary>
    Collides,
}


/// <summary>One line of the "was / becomes" table.</summary>
/// <param name="Companions">The sidecars' own renames, worked out by <see cref="CompanionResolver"/>.</param>
public sealed record RenameRow(
    string Path, string OldName, string NewName, RenameRowStatus Status,
    IReadOnlyList<(string Path, string NewName)> Companions) {

    public bool IsConflict => Status is RenameRowStatus.InvalidName
        or RenameRowStatus.DuplicateInBatch
        or RenameRowStatus.Collides;
}


/// <summary>
/// What the window shows and what OK hands to the file operation service.
/// </summary>
/// <param name="RuleErrorKey">
/// Resource key of a rule that could not be applied at all - a malformed
/// regular expression, a date format .NET rejects; null when the rules
/// are usable. <see cref="RuleErrorDetail"/> carries the runtime's own
/// words about it.
/// </param>
public sealed record RenamePreview(
    IReadOnlyList<RenameRow> Rows, int Changed, int Conflicts,
    string? RuleErrorKey = null, string? RuleErrorDetail = null) {

    public bool CanApply => RuleErrorKey is null && Conflicts == 0 && Changed > 0;


    /// <summary>
    /// Every rename OK means, in table order: each renamed row first, its
    /// companions right behind it - the shape <c>FileOperationService.RenameMany</c>
    /// takes, so the whole batch lands as one undo step.
    /// </summary>
    public IReadOnlyList<(string Path, string NewName)> Plan() {
        var plan = new List<(string, string)>();
        foreach (var row in Rows) {
            if (row.Status != RenameRowStatus.Renamed) {
                continue;
            }
            plan.Add((row.Path, row.NewName));
            plan.AddRange(row.Companions);
        }

        return plan;
    }
}


/// <summary>
/// What the planner needs from the world, handed in rather than looked up:
/// the preview runs on every keystroke and its tests run on nothing.
/// </summary>
/// <param name="Exists">Whether a path is taken on disk (file or folder).</param>
/// <param name="Companions">Names the sidecars after their file; null renames files alone.</param>
/// <param name="ShotDate">
/// EXIF date of a picture, or null when it has none - what <c>[X]</c>
/// prints. Consulted only when the template asks, once per file per
/// preview; the caller caches across previews.
/// </param>
public sealed record RenameContext(
    Func<string, bool> Exists,
    CompanionResolver? Companions = null,
    Func<string, DateTime?>? ShotDate = null);


/// <summary>
/// Applies <see cref="RenameRules"/> to a list of items and says what would
/// happen: the new name of each, and which of them cannot be done. Pure -
/// the world arrives through <see cref="RenameContext"/> - so the window
/// only draws the answer and the rules have tests.
///
/// <para>
/// Order matters twice. The rules apply in their fixed order (find /
/// replace, template, case). The counter runs in item order, which is the
/// list's order on screen: the window says so, and there is no reordering
/// inside it.
/// </para>
///
/// <para>
/// A collision is a name taken by something <em>outside</em> the batch.
/// A name another member of the batch is about to vacate is not one -
/// "a to b, b to a" is a swap, and <c>RenameMany</c> knows how to do it.
/// </para>
/// </summary>
public static class RenamePlanner {
    public const string RegexErrorKey = "RenameErrorRegex";
    public const string DateFormatErrorKey = "RenameErrorDateFormat";

    private const string DefaultDateFormat = "yyyy-MM-dd";

    /// <summary>Windows' cap on a single path component.</summary>
    private const int MaxNameLength = 255;

    /// <summary>
    /// A malicious pattern must not hang the window. A second is far more
    /// than a name of a few dozen characters ever needs.
    /// </summary>
    private static readonly TimeSpan _regexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex _token = new(
        @"\[(N|C|D|X|P)(?::([^\]]*))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<char> _invalidChars = new(Path.GetInvalidFileNameChars());

    private static readonly HashSet<string> _reservedNames = new(StringComparer.OrdinalIgnoreCase) {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };


    public static RenamePreview Preview(RenameRules rules, IReadOnlyList<RenameItem> items, RenameContext context) {
        string[] newNames;
        try {
            newNames = NewNames(rules, items, context);
        } catch (ArgumentException ex) {
            // Regex.Replace and DateTime.ToString both report a bad pattern
            // this way; the message says which.
            return Untouched(items, RegexErrorKey, ex.Message);
        } catch (RegexMatchTimeoutException ex) {
            return Untouched(items, RegexErrorKey, ex.Message);
        } catch (FormatException ex) {
            return Untouched(items, DateFormatErrorKey, ex.Message);
        }

        return Verify(items, newNames, context);
    }


    /// <summary>Whether a name may be given to a file or folder on Windows.</summary>
    public static bool IsValidName(string name) {
        if (name.Length == 0 || name.Length > MaxNameLength || name == "." || name == "..") {
            return false;
        }
        if (name[^1] == '.' || name[^1] == ' ') {
            return false;
        }
        foreach (char c in name) {
            if (_invalidChars.Contains(c)) {
                return false;
            }
        }

        // "CON.txt" is still CON to the kernel.
        int dot = name.IndexOf('.');
        string stem = dot < 0 ? name : name[..dot];

        return !_reservedNames.Contains(stem);
    }


    // --- Step 1: names ------------------------------------------------------

    private static string[] NewNames(RenameRules rules, IReadOnlyList<RenameItem> items, RenameContext context) {
        var names = new string[items.Count];
        Regex? find = rules.Find.Length > 0 && rules.FindIsRegex
            ? new Regex(rules.Find, RegexOptions.CultureInvariant | (rules.FindIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), _regexTimeout)
            : null;

        for (int i = 0; i < items.Count; i++) {
            var item = items[i];
            string stem = item.IsFolder ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
            string extension = item.IsFolder ? string.Empty : Path.GetExtension(item.Name);

            if (find is not null) {
                stem = find.Replace(stem, rules.Replace);
            } else if (rules.Find.Length > 0) {
                stem = stem.Replace(rules.Find, rules.Replace,
                    rules.FindIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }

            long counter = rules.CounterStart + (long)i * rules.CounterStep;
            stem = Expand(rules, item, stem, counter, context);

            names[i] = ApplyCase(stem, rules.NameCase) + ApplyCase(extension, rules.ExtensionCase);
        }

        return names;
    }

    private static string Expand(RenameRules rules, RenameItem item, string stem, long counter, RenameContext context) {
        return _token.Replace(rules.Template, match => {
            string format = match.Groups[2].Success && match.Groups[2].Value.Length > 0
                ? match.Groups[2].Value
                : DefaultDateFormat;

            return match.Groups[1].Value switch {
                "N" => stem,
                "C" => counter.ToString(CultureInfo.InvariantCulture)
                    .PadLeft(Math.Clamp(rules.CounterWidth, 1, RenameRules.MaxCounterWidth), '0'),
                "D" => item.ModifiedUtc.ToLocalTime().ToString(format, CultureInfo.InvariantCulture),
                "X" => (context.ShotDate?.Invoke(item.FullPath) ?? item.ModifiedUtc.ToLocalTime())
                    .ToString(format, CultureInfo.InvariantCulture),
                "P" => Path.GetFileName(Path.GetDirectoryName(item.FullPath) ?? string.Empty),
                _ => match.Value,
            };
        });
    }

    private static string ApplyCase(string text, NameCase rule) {
        if (text.Length == 0) {
            return text;
        }

        return rule switch {
            NameCase.Lower => text.ToLowerInvariant(),
            NameCase.Upper => text.ToUpperInvariant(),
            NameCase.Sentence => SentenceCase(text),
            _ => text,
        };
    }

    /// <summary>
    /// First letter up, the rest down - and for an extension, which starts
    /// with a dot, the letter after the dot.
    /// </summary>
    private static string SentenceCase(string text) {
        int first = text[0] == '.' ? 1 : 0;
        if (first >= text.Length) {
            return text;
        }

        return text[..first]
            + char.ToUpperInvariant(text[first])
            + text[(first + 1)..].ToLowerInvariant();
    }


    // --- Step 2: what can actually be done -------------------------------

    private static RenamePreview Verify(IReadOnlyList<RenameItem> items, string[] newNames, RenameContext context) {
        int count = items.Count;
        var companions = new IReadOnlyList<(string Path, string NewName)>[count];
        var valid = new bool[count];
        var changes = new bool[count];

        // Paths the batch gives up: what is there now under a name that is
        // about to change. A new name landing on one of these is not a
        // collision but a swap or a shift.
        var vacated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // How many rows want each final name - the ones that keep their
        // name included, since a rename onto an unchanged neighbour is a
        // duplicate too.
        var claims = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < count; i++) {
            var item = items[i];
            string dir = Path.GetDirectoryName(item.FullPath) ?? string.Empty;
            changes[i] = !string.Equals(newNames[i], item.Name, StringComparison.Ordinal);
            valid[i] = !changes[i] || IsValidName(newNames[i]);
            companions[i] = changes[i] && valid[i] && context.Companions is { } resolver && item.Companions is { Count: > 0 }
                ? resolver.RenamePlan(item.FullPath, newNames[i], item.Companions).Skip(1).ToArray()
                : Array.Empty<(string, string)>();

            if (!valid[i]) {
                continue;
            }
            if (changes[i] && !string.Equals(newNames[i], item.Name, StringComparison.OrdinalIgnoreCase)) {
                vacated.Add(item.FullPath);
                foreach (var (path, _) in companions[i]) {
                    vacated.Add(path);
                }
            }
            Claim(claims, Path.Combine(dir, newNames[i]));
            foreach (var (path, newName) in companions[i]) {
                Claim(claims, Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, newName));
            }
        }

        var rows = new RenameRow[count];
        int changed = 0;
        int conflicts = 0;
        for (int i = 0; i < count; i++) {
            var item = items[i];
            string dir = Path.GetDirectoryName(item.FullPath) ?? string.Empty;
            var status = RenameRowStatus.Unchanged;

            if (!valid[i]) {
                status = RenameRowStatus.InvalidName;
            } else if (changes[i]) {
                string target = Path.Combine(dir, newNames[i]);
                if (claims[target] > 1 || companions[i].Any(c => claims[Path.Combine(Path.GetDirectoryName(c.Path) ?? string.Empty, c.NewName)] > 1)) {
                    status = RenameRowStatus.DuplicateInBatch;
                } else if (Taken(target, item.FullPath, vacated, context)
                    || companions[i].Any(c => Taken(Path.Combine(Path.GetDirectoryName(c.Path) ?? string.Empty, c.NewName), c.Path, vacated, context))) {
                    status = RenameRowStatus.Collides;
                } else {
                    status = RenameRowStatus.Renamed;
                }
            }

            rows[i] = new RenameRow(item.FullPath, item.Name, newNames[i], status, companions[i]);
            if (status == RenameRowStatus.Renamed) {
                changed++;
            } else if (rows[i].IsConflict) {
                conflicts++;
            }
        }

        return new RenamePreview(rows, changed, conflicts);
    }

    private static void Claim(Dictionary<string, int> claims, string path) {
        claims[path] = claims.TryGetValue(path, out int n) ? n + 1 : 1;
    }

    /// <summary>
    /// Something outside the batch already has this name. A file's own
    /// path does not count (a case-only rename), nor does a path the batch
    /// is about to vacate.
    /// </summary>
    private static bool Taken(string target, string own, HashSet<string> vacated, RenameContext context) {
        if (string.Equals(target, own, StringComparison.OrdinalIgnoreCase) || vacated.Contains(target)) {
            return false;
        }

        return context.Exists(target);
    }

    private static RenamePreview Untouched(IReadOnlyList<RenameItem> items, string errorKey, string detail) {
        var rows = items
            .Select(item => new RenameRow(item.FullPath, item.Name, item.Name, RenameRowStatus.Unchanged, Array.Empty<(string, string)>()))
            .ToArray();

        return new RenamePreview(rows, 0, 0, errorKey, detail);
    }
}
