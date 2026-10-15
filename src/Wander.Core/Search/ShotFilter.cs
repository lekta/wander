namespace Wander.Core.Search;

/// <summary>
/// The box's shooting parameters for the live filter (<see cref="ShotQuery"/>),
/// with what the box says without them: the mask and the type read from
/// the whole name half, the parameter words in the mask. Those apply in a
/// folder where no photo says how it was shot - there the words are a name
/// like any other (decision 2026-10-15).
/// </summary>
/// <param name="Query">The parameters.</param>
/// <param name="PlainName">The mask the box says when they are no parameters.</param>
/// <param name="PlainType">The type, the same.</param>
public sealed record ShotFilter(ShotQuery Query, string PlainName, TypeFilter PlainType) {
    /// <summary>No parameters in the box.</summary>
    public static readonly ShotFilter None = new(ShotQuery.Empty, "", TypeFilter.None);


    /// <summary>The same box: the same words, the same mask and type around them.</summary>
    public bool SameAs(ShotFilter other) {
        return Query.Text == other.Query.Text && PlainName == other.PlainName && PlainType == other.PlainType;
    }
}
