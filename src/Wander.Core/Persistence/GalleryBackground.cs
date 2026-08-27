namespace Wander.Core.Persistence;

/// <summary>
/// What the gallery draws behind the photographs. Three steps rather than a
/// colour picker: the choice is not decorative, it is which surround lets
/// you judge a picture, and the three that matter are "the rest of the
/// window", "neutral grey" and "out of the way entirely".
///
/// <para>
/// Grey is the middle one because it is the one photographers reach for:
/// a light surround makes a photograph look darker and more contrasty than
/// it is, a black one makes it look lighter, and a neutral mid grey biases
/// the eye least. It is also why every serious viewer defaults there.
/// </para>
///
/// <para>
/// This is deliberately the first piece of a palette rather than a
/// one-colour hack: the dark theme on the roadmap is the same idea applied
/// to the whole window, and a switch that flips one hard-coded brush would
/// have to be torn out to get there.
/// </para>
/// </summary>
public enum GalleryBackground {
    /// <summary>The window's own light background — the gallery blends in.</summary>
    Light,

    /// <summary>Neutral mid grey. The default.</summary>
    Grey,

    /// <summary>Near-black, for looking at photographs and nothing else.</summary>
    Dark,
}
