using System.Globalization;
using System.Text.RegularExpressions;

namespace Wander.Core.Search;

/// <summary>A shooting parameter the box can filter photos by.</summary>
public enum ShotParameter {
    Iso,
    Aperture,
    Shutter,
    FocalLength,
}


/// <summary>What a photo says about how it was shot - the numbers the filter compares; null for what it does not say.</summary>
public sealed record ShotFacts(int? Iso, double? FNumber, double? ExposureSeconds, double? FocalLengthMm) {
    /// <summary>Says nothing at all - a picture with no shooting data, a screenshot.</summary>
    public bool IsEmpty => Iso is null && FNumber is null && ExposureSeconds is null && FocalLengthMm is null;


    public double? Of(ShotParameter parameter) {
        return parameter switch {
            ShotParameter.Iso => Iso,
            ShotParameter.Aperture => FNumber,
            ShotParameter.Shutter => ExposureSeconds,
            _ => FocalLengthMm,
        };
    }
}


/// <summary>
/// One parameter within bounds, either open. An exact value is a pair of
/// bounds around it: f/2.8 is recorded as 2.8 by one camera and 2.79 by
/// another, 1/250 as 0.004 or 0.00399.
/// </summary>
public sealed record ShotCondition(ShotParameter Parameter, double? Min, bool MinInclusive, double? Max, bool MaxInclusive) {
    public bool Matches(double value) {
        double slack = Slack(Parameter, value);
        if (Min is { } min && (MinInclusive ? value < min - slack : value <= min + slack)) {
            return false;
        }

        return Max is not { } max || (MaxInclusive ? value <= max + slack : value < max - slack);
    }


    /// <summary>How far a recorded value may be from the one typed and still be it.</summary>
    internal static double Slack(ShotParameter parameter, double value) {
        return parameter switch {
            ShotParameter.Iso => 0.5,
            ShotParameter.Aperture => 0.051,
            ShotParameter.FocalLength => 0.5,
            _ => value * 0.03,
        };
    }
}


/// <summary>
/// Shooting parameters written in the box the way photographers write
/// them (decision 2026-10-15): <c>f/2.8</c>, <c>iso800</c>, <c>50mm</c>,
/// <c>1/250</c>; with <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c> and
/// ranges <c>a..b</c> (<c>a-b</c> for ISO, aperture and focal length):
/// <c>iso&gt;1600</c>, <c>f/1.4..2.8</c>, <c>24-70mm</c>, <c>&gt;=1/1000</c>,
/// <c>1/60..2s</c>. Focal length as recorded, not the 35 mm equivalent.
///
/// <para>
/// A word is a parameter when the whole of it reads as one; anything else
/// stays the name it was. Quotes take a word back to the name -
/// <c>"50mm"</c> - and are dropped from it. Which words count is decided
/// here once per query; whether they apply is the folder's - with no photo
/// saying anything they are the name again (<see cref="Listing.SearchController"/>).
/// </para>
/// </summary>
public sealed class ShotQuery {
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex _iso = new(@"^iso=?(?<c>.+)$", Options);
    private static readonly Regex _aperture = new(@"^f(?:/(?<c>.+)|(?<c>[<>].+))$", Options);
    private static readonly Regex _focal = new(@"^(?<c>.+?)mm$", Options);
    private static readonly Regex _number = new(@"^\d+(?:[.,]\d+)?$", Options);
    private static readonly Regex _time = new(@"^(?:1/(?<d>\d+(?:[.,]\d+)?)s?|(?<s>\d+(?:[.,]\d+)?)(?:s|""))$", Options);

    private static readonly char[] _space = { ' ', '\t' };


    private ShotQuery(IReadOnlyList<ShotCondition> conditions, string text) {
        Conditions = conditions;
        Text = text;
    }


    /// <summary>No parameters.</summary>
    public static ShotQuery Empty { get; } = new(Array.Empty<ShotCondition>(), "");

    public IReadOnlyList<ShotCondition> Conditions { get; }

    /// <summary>The words the conditions were read from, as typed.</summary>
    public string Text { get; }

    public bool IsEmpty => Conditions.Count == 0;


    /// <summary>
    /// Takes the parameter words out of the name half of the box: what is
    /// left goes on as the mask and the type, the words as the query.
    /// </summary>
    public static (string Remainder, ShotQuery Query) Extract(string? head) {
        if (string.IsNullOrWhiteSpace(head)) {
            return (head ?? "", Empty);
        }

        var rest = new List<string>();
        var words = new List<string>();
        var conditions = new List<ShotCondition>();
        bool quoted = false;
        foreach (string word in head.Split(_space, StringSplitOptions.RemoveEmptyEntries)) {
            if (word.Length > 2 && word[0] == '"' && word[^1] == '"') {
                rest.Add(word[1..^1]);
                quoted = true;
            } else if (TryParse(word) is { } condition) {
                conditions.Add(condition);
                words.Add(word);
            } else {
                rest.Add(word);
            }
        }

        if (conditions.Count == 0) {
            return (quoted ? string.Join(' ', rest) : head, Empty);
        }

        return (string.Join(' ', rest), new ShotQuery(conditions, string.Join(' ', words)));
    }

    /// <summary>One word as a condition, or null when it is not one.</summary>
    public static ShotCondition? TryParse(string word) {
        if (_iso.Match(word) is { Success: true } iso) {
            return Condition(ShotParameter.Iso, iso.Groups["c"].Value, Number, dashRange: true);
        }
        if (_aperture.Match(word) is { Success: true } aperture) {
            return Condition(ShotParameter.Aperture, aperture.Groups["c"].Value, Number, dashRange: true);
        }
        if (_focal.Match(word) is { Success: true } focal) {
            return Condition(ShotParameter.FocalLength, focal.Groups["c"].Value, Number, dashRange: true);
        }

        // A time only with its mark - a "1/" or an "s": a bare number is no
        // parameter of anything.
        return word.Contains('/') || word.EndsWith('s') || word.EndsWith('S') || word.EndsWith('"')
            ? Condition(ShotParameter.Shutter, word, Seconds, dashRange: false)
            : null;
    }


    /// <summary>All the conditions hold for <paramref name="facts"/>; a photo that does not say one of them is not a match.</summary>
    public bool Matches(ShotFacts? facts) {
        if (facts is null) {
            return IsEmpty;
        }

        foreach (var condition in Conditions) {
            if (facts.Of(condition.Parameter) is not { } value || !condition.Matches(value)) {
                return false;
            }
        }

        return true;
    }


    private static ShotCondition? Condition(ShotParameter parameter, string text, Func<string, double?> value, bool dashRange) {
        if (text.StartsWith(">=", StringComparison.Ordinal)) {
            return value(text[2..]) is { } v ? new ShotCondition(parameter, v, true, null, false) : null;
        }
        if (text.StartsWith("<=", StringComparison.Ordinal)) {
            return value(text[2..]) is { } v ? new ShotCondition(parameter, null, false, v, true) : null;
        }
        if (text.StartsWith('>')) {
            return value(text[1..]) is { } v ? new ShotCondition(parameter, v, false, null, false) : null;
        }
        if (text.StartsWith('<')) {
            return value(text[1..]) is { } v ? new ShotCondition(parameter, null, false, v, false) : null;
        }

        int range = text.IndexOf("..", StringComparison.Ordinal);
        int length = 2;
        if (range < 0 && dashRange && text.IndexOf('-') is > 0 and var dash) {
            range = dash;
            length = 1;
        }
        if (range >= 0) {
            string from = text[..range];
            string to = text[(range + length)..];
            double? min = from.Length > 0 ? value(from) : null;
            double? max = to.Length > 0 ? value(to) : null;
            bool broken = (from.Length > 0 && min is null) || (to.Length > 0 && max is null) || (min is null && max is null);

            return broken ? null : new ShotCondition(parameter, Lower(min, max), true, Upper(min, max), true);
        }

        return value(text) is { } exact ? new ShotCondition(parameter, exact, true, exact, true) : null;
    }

    // A range typed high to low is the same range.
    private static double? Lower(double? a, double? b) {
        return a is { } x && b is { } y ? Math.Min(x, y) : a;
    }

    private static double? Upper(double? a, double? b) {
        return a is { } x && b is { } y ? Math.Max(x, y) : b;
    }

    private static double? Number(string text) {
        return _number.IsMatch(text) ? double.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture) : null;
    }

    private static double? Seconds(string text) {
        if (_time.Match(text) is not { Success: true } time) {
            return null;
        }

        if (time.Groups["d"].Success) {
            double denominator = double.Parse(time.Groups["d"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);

            return denominator > 0 ? 1 / denominator : null;
        }

        return double.Parse(time.Groups["s"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
    }
}
