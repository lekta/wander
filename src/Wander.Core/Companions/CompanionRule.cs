namespace Wander.Core.Companions;

/// <summary>
/// How a companion's file name is derived from the name of the file it
/// belongs to. Both shapes exist in the wild and neither can be dropped:
/// Unity / RawTherapee append, Adobe / darktable replace.
/// </summary>
public enum CompanionNaming {
    /// <summary>Suffix appended to the whole name: <c>Sprite.png</c> → <c>Sprite.png.meta</c>.</summary>
    Appended,

    /// <summary>Suffix replaces the extension: <c>IMG_1234.CR2</c> → <c>IMG_1234.xmp</c>.</summary>
    Replaced,
}


/// <summary>
/// One companion format. <see cref="CompanionResolver"/> holds a list of
/// these; a format is nothing but a suffix plus the naming shape, so
/// adding support for <c>.xmp</c> or <c>.srt</c> later is one more entry
/// in that list rather than new code.
/// </summary>
/// <param name="Suffix">Including the dot, e.g. <c>.meta</c>.</param>
/// <param name="Naming">Appended or replaced — see <see cref="CompanionNaming"/>.</param>
/// <param name="Label">Human-readable name for the UI ("Unity .meta").</param>
public sealed record CompanionRule(string Suffix, CompanionNaming Naming, string Label) {
    /// <summary>Name this rule's companion would have for a main file called <paramref name="mainName"/>.</summary>
    public string CompanionNameFor(string mainName) {
        return Naming == CompanionNaming.Appended
            ? mainName + Suffix
            : Path.GetFileNameWithoutExtension(mainName) + Suffix;
    }


    /// <summary>
    /// Reverse direction: does <paramref name="fileName"/> look like this
    /// rule's companion, and if so what identifies its main file?
    /// <para>
    /// The key is the full main-file name for <see cref="CompanionNaming.Appended"/>
    /// and the bare stem for <see cref="CompanionNaming.Replaced"/> — the
    /// extension of the main file is simply not recoverable in the second
    /// case, so the resolver has to look it up by stem.
    /// </para>
    /// </summary>
    public bool TryMatch(string fileName, out string mainKey) {
        mainKey = "";
        if (!fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        string stripped = fileName[..^Suffix.Length];
        if (stripped.Length == 0) {
            return false;
        }

        // Appended: what is left *is* the main file's name.
        // Replaced:  what is left is its stem; the resolver matches on that.
        mainKey = stripped;
        return true;
    }
}
