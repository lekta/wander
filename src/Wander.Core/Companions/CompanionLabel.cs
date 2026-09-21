namespace Wander.Core.Companions;

/// <summary>
/// How the sidecars folded into a row are mentioned beside its name:
/// <c>(+.xmp)</c> - what is added to the file's name to get theirs, and
/// nothing else. The preview footer used to spend a line of its own on
/// their full names, which made a photograph with a sidecar a line taller
/// than the one without (2026-09-21).
/// </summary>
public static class CompanionLabel {
    /// <param name="fileName">Name of the main file, with its extension.</param>
    /// <param name="companions">Paths or names of its companions; may be null.</param>
    /// <returns>"(+.xmp)", "(+.xmp, .pp3)", or an empty string when there are none.</returns>
    public static string For(string fileName, IReadOnlyList<string>? companions) {
        if (companions is null || companions.Count == 0) {
            return "";
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        var parts = new List<string>(companions.Count);
        foreach (string companion in companions) {
            string name = Path.GetFileName(companion);

            // IMG.CR2.pp3 beside IMG.CR2 adds ".pp3"; IMG.xmp beside it adds
            // ".xmp" to the stem; anything named otherwise is its extension,
            // or its whole name when it has none.
            string part = name.StartsWith(fileName, StringComparison.OrdinalIgnoreCase) && name.Length > fileName.Length
                ? name[fileName.Length..]
                : name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) && name.Length > stem.Length
                    ? name[stem.Length..]
                    : Path.GetExtension(name) is { Length: > 0 } extension ? extension : name;
            if (!parts.Contains(part, StringComparer.OrdinalIgnoreCase)) {
                parts.Add(part);
            }
        }

        return "(+" + string.Join(", ", parts) + ")";
    }
}
