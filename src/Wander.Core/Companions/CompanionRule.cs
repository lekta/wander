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
/// <param name="Versions">
/// An appended rule that also takes the editor's numbered versions of the
/// file: darktable's duplicate <c>IMG_1234_01.CR2.xmp</c> is
/// <c>IMG_1234.CR2</c>'s (decision 2026-10-01) - unless a file
/// <c>IMG_1234_01.CR2</c> is there to own it by its exact name.
/// </param>
public sealed record CompanionRule(string Suffix, CompanionNaming Naming, string Label, bool Versions = false) {
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


    /// <summary>
    /// Whether <paramref name="fileName"/> is a numbered version's companion
    /// (<see cref="Versions"/>), and of which file: <c>IMG_01.CR2.xmp</c> →
    /// <c>IMG.CR2</c>, the version <c>_01</c>. Two digits or more, as
    /// darktable writes them.
    /// </summary>
    public bool TryVersion(string fileName, out string mainName, out string version) {
        mainName = version = "";
        if (!Versions || Naming != CompanionNaming.Appended || !TryMatch(fileName, out string key)) {
            return false;
        }

        string extension = Path.GetExtension(key);
        string stem = key[..^extension.Length];
        int mark = stem.LastIndexOf('_');
        if (mark <= 0 || stem.Length - mark - 1 < 2 || !stem[(mark + 1)..].All(char.IsAsciiDigit)) {
            return false;
        }

        mainName = stem[..mark] + extension;
        version = stem[mark..];
        return true;
    }


    /// <summary>The name a version's companion takes when its main file is renamed: <c>NEW_01.CR2.xmp</c>.</summary>
    public string VersionNameFor(string mainName, string version) {
        return Path.GetFileNameWithoutExtension(mainName) + version + Path.GetExtension(mainName) + Suffix;
    }
}
