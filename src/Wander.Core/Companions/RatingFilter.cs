using Wander.Core.FileSystem;

namespace Wander.Core.Companions;

/// <summary>
/// The gallery's filter over how photographs are marked up: "at least this
/// many stars", "this colour label", or both. A value, not a service — the
/// filtering itself is one predicate, and keeping it a record means the
/// filter bar, the listing pipeline and the tests all hold the same thing.
/// </summary>
/// <param name="MinRank">
/// Lowest star count that passes, 0…5. Zero means "any", including
/// unrated — a filter that hides unrated files by default would hide most
/// of a fresh import.
/// </param>
/// <param name="ColorLabel">
/// Colour index that passes, 1…5, or null for "any colour". There is
/// deliberately no "unlabelled only": the swatch row has five swatches and
/// no sixth thing to click.
/// </param>
public sealed record RatingFilter(int MinRank, int? ColorLabel) {
    /// <summary>Everything passes. The state the filter bar starts and resets to.</summary>
    public static readonly RatingFilter None = new(0, null);


    /// <summary>True when this filter actually removes anything.</summary>
    public bool IsActive => MinRank > 0 || ColorLabel is not null;


    public bool Matches(FileSystemEntry entry) {
        if (!IsActive) {
            return true;
        }

        var rating = entry.Rating;
        if (MinRank > 0 && (rating?.Rank ?? 0) < MinRank) {
            return false;
        }
        if (ColorLabel is int wanted && (rating?.ColorLabel ?? 0) != wanted) {
            return false;
        }

        return true;
    }
}
