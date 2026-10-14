using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Search;

namespace Wander.Core.Tests;

public class TypeFilterTests {
    // Keywords and names as the box would know them in the Russian
    // interface: two names start alike on purpose, as audio and archives
    // do there, and their keywords do not.
    private static readonly IReadOnlyList<TypeName> _names = new[] {
        new TypeName(FileTypeGroup.Images, "img", "изображения"),
        new TypeName(FileTypeGroup.Audio, "aud", "аудио"),
        new TypeName(FileTypeGroup.TextAndCode, "txt", "текст и код"),
        new TypeName(FileTypeGroup.Documents, "doc", "документы"),
        new TypeName(FileTypeGroup.Archives, "zip", "архивы"),
        new TypeName(FileTypeGroup.Folders, "dir", "папки"),
    };


    [Theory]
    [InlineData("img", FileTypeGroup.Images)]
    [InlineData("i", FileTypeGroup.Images)]
    [InlineData("IM", FileTypeGroup.Images)]
    [InlineData("z", FileTypeGroup.Archives)]
    [InlineData("t", FileTypeGroup.TextAndCode)]
    public void Parse_ByTheStartOfTheKeyword(string word, FileTypeGroup group) {
        Assert.Equal(TypeFilter.Of(group), TypeFilter.Parse(word, _names));
    }


    [Theory]
    [InlineData("изображения", FileTypeGroup.Images)]
    [InlineData("изо", FileTypeGroup.Images)]
    [InlineData("ИЗО", FileTypeGroup.Images)]
    [InlineData("ар", FileTypeGroup.Archives)]
    [InlineData("текст", FileTypeGroup.TextAndCode)]
    [InlineData("п", FileTypeGroup.Folders)]
    public void Parse_ByTheStartOfTheName(string word, FileTypeGroup group) {
        Assert.Equal(TypeFilter.Of(group), TypeFilter.Parse(word, _names));
    }


    [Fact]
    public void Parse_TwoStartAlike_TheFirstListedWins() {
        // What the list lights first is what filters: a Cyrillic "а" starts
        // two names, a Latin "d" two keywords.
        Assert.Equal(TypeFilter.Of(FileTypeGroup.Audio), TypeFilter.Parse("а", _names));
        Assert.Equal(TypeFilter.Of(FileTypeGroup.Documents), TypeFilter.Parse("d", _names));
    }


    [Fact]
    public void Candidates_LatinAndCyrillicLettersAreDifferentWords() {
        Assert.Equal(new[] { _names[1] }, TypeFilter.Candidates("a", _names));
        Assert.Equal(new[] { _names[1], _names[4] }, TypeFilter.Candidates(" а ", _names));
    }


    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Parse_NoWord_IsNone(string? word) {
        Assert.Same(TypeFilter.None, TypeFilter.Parse(word, _names));
        Assert.True(TypeFilter.None.IsEmpty);
    }


    [Fact]
    public void Parse_UnknownWord_LetsNothingThrough() {
        var filter = TypeFilter.Parse("xyz", _names);

        Assert.Equal(TypeFilter.Unknown, filter);
        Assert.False(filter.IsEmpty);
        Assert.False(filter.Matches(File("a.jpg")));
        Assert.False(filter.Matches(Dir("d")));
    }


    [Fact]
    public void Candidates_AllForNoWord_InOrder() {
        Assert.Equal(_names, TypeFilter.Candidates("", _names));
        Assert.Empty(TypeFilter.Candidates("q", _names));
    }


    [Fact]
    public void IsSpelledBy_TheWholeKeywordOrName() {
        Assert.True(_names[0].IsSpelledBy("IMG"));
        Assert.True(_names[0].IsSpelledBy("изображения"));
        Assert.False(_names[0].IsSpelledBy("im"));
    }


    [Fact]
    public void Names_EveryGroupButAll_KeywordsDistinct() {
        var names = TypeFilter.Names();

        Assert.Equal(
            Enum.GetValues<FileTypeGroup>().Where(group => group != FileTypeGroup.All),
            names.Select(name => name.Group).Order());
        Assert.Equal(names.Count, names.Select(name => name.Keyword).Distinct().Count());
    }


    [Fact]
    public void Matches_TheGroup_FoldersOnlyForFolders() {
        Assert.True(TypeFilter.None.Matches(Dir("d")));
        Assert.True(TypeFilter.Of(FileTypeGroup.Images).Matches(File("a.JPG")));
        Assert.False(TypeFilter.Of(FileTypeGroup.Images).Matches(File("a.txt")));
        Assert.False(TypeFilter.Of(FileTypeGroup.Images).Matches(Dir("shots.jpg")));
        Assert.True(TypeFilter.Of(FileTypeGroup.Folders).Matches(Dir("d")));
    }


    [Fact]
    public void Of_All_IsNone() {
        Assert.Same(TypeFilter.None, TypeFilter.Of(FileTypeGroup.All));
    }


    private static FileSystemEntry File(string name) {
        return Entry(name, EntryKind.File);
    }


    private static FileSystemEntry Dir(string name) {
        return Entry(name, EntryKind.Directory);
    }


    private static FileSystemEntry Entry(string name, EntryKind kind) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\folder\" + name,
            Kind: kind,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }
}
