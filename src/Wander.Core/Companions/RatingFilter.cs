using Wander.Core.FileSystem;

namespace Wander.Core.Companions;

/// <summary>
/// The gallery's filter over how photographs are marked up: which star
/// counts pass and which colour labels pass. A value, not a service — the
/// filtering itself is one predicate, and keeping it a record means the
/// filter bar, the listing pipeline and the tests all hold the same thing.
///
/// <para>
/// <b>Sets, not thresholds.</b> It started as "at least N stars", which is
/// the question you ask when deciding what to keep — and only that question.
/// "What did I leave at two stars to come back to" is a different one, and
/// so is "what have I not looked at yet". Both fall out of holding the set
/// of ranks that pass instead of a lower bound: a plain click on a star
/// selects it and everything above it, and a modified click adds or removes
/// one rank on its own.
/// </para>
///
/// <para>
/// Rank 0 is a member like any other and means <em>unrated</em> — a photo
/// with no sidecar or with a sidecar that says nothing. It is the one rank
/// that a plain click selects alone: "unrated and above" would be every
/// photograph in the folder, which is not a filter.
/// </para>
/// </summary>
/// <param name="Ranks">
/// Bit <c>i</c> set means star count <c>i</c> passes, 0…5. Zero means the
/// stars are not filtering at all.
/// </param>
/// <param name="Colors">
/// Bit <c>i</c> set means colour label <c>i</c> passes, 1…5. Zero means the
/// colours are not filtering at all.
/// </param>
public sealed record RatingFilter(int Ranks, int Colors) {
    /// <summary>Highest star count, mirroring <see cref="Pp3Sidecar.MaxRank"/>.</summary>
    public const int MaxRank = 5;

    /// <summary>The rank that means "not rated" — the crossed-out star in the bar.</summary>
    public const int Unrated = 0;

    /// <summary>Everything passes. The state the filter bar starts and resets to.</summary>
    public static readonly RatingFilter None = new(0, 0);


    /// <summary>True when this filter actually removes anything.</summary>
    public bool IsActive => Ranks != 0 || Colors != 0;

    /// <summary>Whether star number <paramref name="rank"/> reads as lit in the filter bar.</summary>
    public bool HasRank(int rank) {
        return (Ranks & Bit(rank)) != 0;
    }

    /// <summary>Whether colour <paramref name="color"/> reads as chosen in the filter bar.</summary>
    public bool HasColor(int color) {
        return (Colors & Bit(color)) != 0;
    }


    /// <summary>
    /// A plain click on a star: that rank and everything above it, and
    /// nothing else. Clicking the star whose run is already exactly what is
    /// selected turns the star filter off again — the same "click what is
    /// set to unset it" the rating widget itself uses, and the only way to
    /// clear one half of the bar without clearing the other.
    /// </summary>
    public RatingFilter PickRank(int rank) {
        int wanted = rank == Unrated ? Bit(Unrated) : RunFrom(rank);

        return this with { Ranks = Ranks == wanted ? 0 : wanted };
    }


    /// <summary>
    /// A modified click on a star: that one rank joins or leaves the set,
    /// whatever else is in it. This is how "three and up, but not five"
    /// gets said.
    /// </summary>
    public RatingFilter ToggleRank(int rank) {
        return this with { Ranks = Ranks ^ Bit(rank) };
    }


    /// <summary>A plain click on a swatch: that colour and no other.</summary>
    public RatingFilter PickColor(int color) {
        int wanted = Bit(color);

        return this with { Colors = Colors == wanted ? 0 : wanted };
    }


    /// <summary>A modified click on a swatch: that colour joins or leaves the set.</summary>
    public RatingFilter ToggleColor(int color) {
        return this with { Colors = Colors ^ Bit(color) };
    }


    public bool Matches(FileSystemEntry entry) {
        if (!IsActive) {
            return true;
        }

        var rating = entry.Rating;
        if (Ranks != 0 && !HasRank(rating?.Rank ?? Unrated)) {
            return false;
        }
        if (Colors != 0 && !HasColor(rating?.ColorLabel ?? 0)) {
            return false;
        }

        return true;
    }


    /// <summary>Bit for one rank or colour; anything out of range is no bit at all.</summary>
    private static int Bit(int index) {
        return index >= 0 && index <= MaxRank ? 1 << index : 0;
    }

    /// <summary>Bits for <paramref name="from"/> up to <see cref="MaxRank"/>.</summary>
    private static int RunFrom(int from) {
        int bits = 0;
        for (int i = Math.Max(from, 1); i <= MaxRank; i++) {
            bits |= Bit(i);
        }

        return bits;
    }
}
