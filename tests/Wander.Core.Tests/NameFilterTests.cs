using Wander.Core.Search;
using Xunit;

namespace Wander.Core.Tests;

public class NameFilterTests {
    [Fact]
    public void Empty_MatchesEverything() {
        var filter = NameFilter.Parse("");

        Assert.True(filter.IsEmpty);
        Assert.True(filter.Matches("anything.txt"));
    }


    [Fact]
    public void WhitespaceOnly_IsEmpty() {
        Assert.True(NameFilter.Parse("   ").IsEmpty);
        Assert.True(NameFilter.Parse(null).IsEmpty);
        // A half-typed separator must not blank the list.
        Assert.True(NameFilter.Parse(";").IsEmpty);
    }


    // --- Substring -----------------------------------------------------

    [Fact]
    public void WithoutWildcards_IsSubstring() {
        var filter = NameFilter.Parse("doc");

        Assert.False(filter.HasWildcards);
        Assert.True(filter.Matches("mydoc.txt"));
        Assert.True(filter.Matches("DOCUMENT.md"));
        Assert.False(filter.Matches("readme.txt"));
    }


    [Fact]
    public void Substring_FindsDotT_InExtension() {
        // What the user typed expecting "extension starting with t". As a
        // substring it does find .txt — and it also finds a name with ".t"
        // in the middle, which is why the wildcard form exists.
        var filter = NameFilter.Parse(".t");

        Assert.True(filter.Matches("ajajaja.txt"));
        Assert.True(filter.Matches("archive.tar.gz"));
        Assert.False(filter.Matches("chat_export.pdf"));
        Assert.False(filter.Matches("skill - text.md"));
    }


    // --- Wildcards -----------------------------------------------------

    [Fact]
    public void Star_AnchorsToTheWholeName() {
        var filter = NameFilter.Parse("*.cs");

        Assert.True(filter.HasWildcards);
        Assert.True(filter.Matches("Program.cs"));
        Assert.False(filter.Matches("Program.cs.bak"));
        Assert.False(filter.Matches("cs"));
    }


    [Fact]
    public void Star_MatchesTheExtensionQuestionFromTheField() {
        // "*.t*" is what ".t" was reaching for.
        var filter = NameFilter.Parse("*.t*");

        Assert.True(filter.Matches("ajajaja.txt"));
        Assert.False(filter.Matches("chat_export.pdf"));
        Assert.False(filter.Matches("skill - text.md"));
    }


    [Fact]
    public void Question_IsExactlyOneCharacter() {
        var filter = NameFilter.Parse("IMG_?.jpg");

        Assert.True(filter.Matches("IMG_1.jpg"));
        Assert.False(filter.Matches("IMG_12.jpg"));
        Assert.False(filter.Matches("IMG_.jpg"));
    }


    [Fact]
    public void Star_MatchesNothingAsWellAsSomething() {
        var filter = NameFilter.Parse("a*b");

        Assert.True(filter.Matches("ab"));
        Assert.True(filter.Matches("axxxb"));
        Assert.False(filter.Matches("axxx"));
    }


    [Fact]
    public void Star_IsCaseInsensitive() {
        Assert.True(NameFilter.Parse("*.CS").Matches("Program.cs"));
        Assert.True(NameFilter.Parse("*.cs").Matches("PROGRAM.CS"));
        Assert.True(NameFilter.Parse("*документ*").Matches("Мой ДОКУМЕНТ.txt"));
    }


    [Fact]
    public void Star_DoesNotBlowUpOnAdversarialPattern() {
        // The shape that makes a regex translation backtrack forever.
        var filter = NameFilter.Parse("*a*a*a*a*a*a*a*b");

        Assert.False(filter.Matches(new string('a', 64)));
    }


    [Fact]
    public void BareStar_MatchesEverything() {
        var filter = NameFilter.Parse("*");

        Assert.False(filter.IsEmpty);
        Assert.True(filter.Matches("anything"));
        Assert.True(filter.Matches(""));
    }


    // --- Several patterns ----------------------------------------------

    [Fact]
    public void Semicolon_SeparatesAlternatives() {
        var filter = NameFilter.Parse("*.cs;*.xaml");

        Assert.True(filter.Matches("Program.cs"));
        Assert.True(filter.Matches("MainWindow.xaml"));
        Assert.False(filter.Matches("readme.md"));
    }


    [Fact]
    public void Semicolon_MixesBothLanguages() {
        var filter = NameFilter.Parse("*.cs; readme");

        Assert.True(filter.Matches("Program.cs"));
        Assert.True(filter.Matches("README.md"));
        Assert.False(filter.Matches("notes.txt"));
    }


    [Fact]
    public void Text_RoundTripsVerbatim() {
        // The box shows what was typed, spacing and all.
        Assert.Equal("*.cs; readme", NameFilter.Parse("*.cs; readme").Text);
    }
}
