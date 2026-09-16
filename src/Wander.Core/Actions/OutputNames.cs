namespace Wander.Core.Actions;

/// <summary>
/// Where a declared output goes: beside its source, under the name the
/// template gives (<c>{name}</c> the source's name without extension,
/// <c>{ext}</c> its extension without the dot), and never over anything -
/// a taken name gets " (1)" before the extension, the way a copy does.
/// The source itself counts as taken, so <c>{name}.{ext}</c> yields
/// "photo (1).jpg", not a program writing over its own input.
/// </summary>
public static class OutputNames {
    public static string Resolve(string template, string sourcePath, Func<string, bool> exists) {
        string dir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string name = template
            .Replace("{name}", Path.GetFileNameWithoutExtension(sourcePath), StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", Path.GetExtension(sourcePath).TrimStart('.'), StringComparison.OrdinalIgnoreCase);

        string candidate = Path.Combine(dir, name);
        if (!exists(candidate)) {
            return candidate;
        }

        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int i = 1; ; i++) {
            candidate = Path.Combine(dir, $"{stem} ({i}){extension}");
            if (!exists(candidate)) {
                return candidate;
            }
        }
    }
}
