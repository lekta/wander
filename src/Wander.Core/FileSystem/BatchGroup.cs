namespace Wander.Core.FileSystem;

/// <summary>
/// One thing the user thinks they are copying / moving: a main file, plus
/// the companion sidecars that travel with it.
///
/// <para>
/// Batches are expressed in groups rather than in paths so that a conflict
/// is asked about once. Dropping <c>Sprite.png</c> onto a folder that
/// already holds both it and <c>Sprite.png.meta</c> is <em>one</em>
/// collision from where the user is sitting, and answering "replace" twice
/// for one drag is the kind of thing that makes people stop reading dialogs.
/// </para>
///
/// <para>
/// The group carries no notion of what a companion format is: when a
/// conflict is auto-renamed, the members' new names are derived from the
/// main file's new name by substituting the part they share. That keeps
/// <see cref="BatchExecutor"/> free of format knowledge and works for both
/// naming shapes (<c>Sprite.png.meta</c> and <c>IMG_1234.xmp</c>).
/// </para>
/// </summary>
public sealed record BatchGroup(string Primary, IReadOnlyList<string> Companions) {
    /// <summary>A lone file — the shape every caller that knows nothing about companions uses.</summary>
    public static BatchGroup Single(string path) {
        return new BatchGroup(path, Array.Empty<string>());
    }


    /// <summary>Main file first, then its companions. The order operations are applied in.</summary>
    public IEnumerable<string> All {
        get {
            yield return Primary;
            foreach (string companion in Companions) {
                yield return companion;
            }
        }
    }
}
