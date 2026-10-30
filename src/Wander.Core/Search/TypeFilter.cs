using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Localization;

namespace Wander.Core.Search;

/// <summary>
/// The <c>/тип</c> part of a search: which kind of file passes.
///
/// <para>
/// The groups are the actions' (<see cref="FileTypeGroups"/>), not a list
/// of search's own: "/vid" and an action "for video" agree about a
/// <c>.mkv</c>, and the preview pane with both.
/// </para>
///
/// <para>
/// Each group has a keyword - <c>img</c>, <c>doc</c> - and a name in the
/// interface language; a word is read by the start of either (decision of
/// 2026-10-14): the keyword is short in the box and the same in every
/// language, the name is what a Russian keyboard types without switching -
/// <c>/i</c> and <c>/и</c> are both pictures. Where two start alike, the
/// first in <see cref="Names"/> wins: the order the box lists them in, so
/// what is lit in the list is what filters.
/// </para>
///
/// <para>
/// A word that starts nothing lets nothing through. The box says what the
/// list is filtered by, and <c>/xyz</c> over the whole folder would be a
/// filter the box claims and the list does not apply.
/// </para>
/// </summary>
public sealed record TypeFilter {
    /// <summary>No type asked for: everything passes.</summary>
    public static readonly TypeFilter None = new(FileTypeGroup.All);

    /// <summary>A word that names no group: nothing passes.</summary>
    public static readonly TypeFilter Unknown = new((FileTypeGroup?)null);

    // What "/" offers, in the order it offers it, with the keywords: the
    // actions' groups but "all", which is what no type already says.
    private static readonly (FileTypeGroup Group, string Keyword)[] _offered = {
        (FileTypeGroup.Images, "img"), (FileTypeGroup.Raw, "raw"), (FileTypeGroup.Video, "vid"), (FileTypeGroup.Audio, "aud"),
        (FileTypeGroup.TextAndCode, "txt"), (FileTypeGroup.Documents, "doc"), (FileTypeGroup.Archives, "zip"),
        (FileTypeGroup.Folders, "dir"),
    };


    private TypeFilter(FileTypeGroup? group) {
        Group = group;
    }


    /// <summary>The group that passes: <see cref="FileTypeGroup.All"/> for none asked, null for an unknown word.</summary>
    public FileTypeGroup? Group { get; }

    /// <summary>True when this lets everything through.</summary>
    public bool IsEmpty => Group == FileTypeGroup.All;


    public static TypeFilter Of(FileTypeGroup group) {
        return group == FileTypeGroup.All ? None : new TypeFilter(group);
    }


    /// <summary>The groups a type word can name, with their keywords and their names in the interface language.</summary>
    public static IReadOnlyList<TypeName> Names() {
        return _offered
            .Select(offer => new TypeName(offer.Group, offer.Keyword, Text.Get(FileTypeGroups.NameKey(offer.Group))))
            .ToArray();
    }


    /// <summary>
    /// The groups whose keyword or name <paramref name="word"/> is the start
    /// of, in the order of <paramref name="names"/>.
    /// </summary>
    public static IReadOnlyList<TypeName> Candidates(string? word, IReadOnlyList<TypeName> names) {
        word = word?.Trim() ?? "";

        return names
            .Where(name => name.Keyword.StartsWith(word, StringComparison.OrdinalIgnoreCase)
                || name.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }


    /// <summary>What a typed word filters by: the first group it starts; <see cref="None"/> for no word.</summary>
    public static TypeFilter Parse(string? word, IReadOnlyList<TypeName> names) {
        if (string.IsNullOrWhiteSpace(word)) {
            return None;
        }

        var candidates = Candidates(word, names);

        return candidates.Count == 0 ? Unknown : Of(candidates[0].Group);
    }


    public bool Matches(FileSystemEntry entry) {
        return Group is { } group && FileTypeGroups.Matches(group, entry);
    }
}


/// <summary>
/// A group of files and the words the search box knows it by: the keyword
/// it writes, the name in the interface language it also reads.
/// </summary>
public readonly record struct TypeName(FileTypeGroup Group, string Keyword, string Name) {
    /// <summary>True when <paramref name="word"/> is the keyword or the name, whole.</summary>
    public bool IsSpelledBy(string word) {
        return string.Equals(Keyword, word, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Name, word, StringComparison.OrdinalIgnoreCase);
    }
}
