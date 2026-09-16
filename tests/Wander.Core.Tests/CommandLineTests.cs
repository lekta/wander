using Wander.Core.Actions;

namespace Wander.Core.Tests;

public class CommandLineTests {
    private const string Clip = @"D:\My Videos\clip one.mp4";


    [Fact]
    public void EveryPlaceholder_IsQuoted() {
        string line = CommandLine.Expand("-i {path} {name} {ext} {dir}", new[] { Clip });

        Assert.Equal(@"-i ""D:\My Videos\clip one.mp4"" ""clip one"" ""mp4"" ""D:\My Videos""", line);
    }

    [Fact]
    public void Placeholders_AreCaseInsensitive_AndUnknownOnesStay() {
        string line = CommandLine.Expand("{PATH} {other}", new[] { Clip });

        Assert.Equal(@"""D:\My Videos\clip one.mp4"" {other}", line);
    }

    [Fact]
    public void Paths_ListsEverySelectedFile() {
        string line = CommandLine.Expand("tool {paths}", new[] { @"C:\a.txt", @"C:\b c.txt" });

        Assert.Equal(@"tool ""C:\a.txt"" ""C:\b c.txt""", line);
    }

    [Fact]
    public void InOneCommandMode_TheSingleFilePlaceholders_MeanTheFirstFile() {
        string line = CommandLine.Expand("{name}", new[] { @"C:\first.txt", @"C:\second.txt" });

        Assert.Equal(@"""first""", line);
    }

    [Fact]
    public void ListAndOut_ExpandOnlyWhenGiven() {
        Assert.Equal(@"@""C:\tmp\list.txt"" -o ""C:\out.mp4""",
            CommandLine.Expand("@{list} -o {out}", new[] { Clip }, @"C:\tmp\list.txt", @"C:\out.mp4"));
        Assert.Equal("@ -o ", CommandLine.Expand("@{list} -o {out}", new[] { Clip }));
    }

    [Fact]
    public void Quote_DoublesATrailingBackslash_SoARootDoesNotEatTheClosingQuote() {
        Assert.Equal(@"""D:\\""", CommandLine.Quote(@"D:\"));
        Assert.Equal(@"""D:\x""", CommandLine.Quote(@"D:\x"));
        Assert.Equal(@"""D:\x""", CommandLine.Expand("{dir}", new[] { @"D:\x\y.txt" }));
        Assert.Equal(@"""D:\\""", CommandLine.Expand("{dir}", new[] { @"D:\y.txt" }));
    }

    [Fact]
    public void Validation_CatchesTheModeMismatch() {
        Assert.Null(CommandLine.ValidationKey("-i {path}", runPerFile: true));
        Assert.Null(CommandLine.ValidationKey("{paths}", runPerFile: false));
        Assert.Null(CommandLine.ValidationKey("@{list}", runPerFile: false));
        Assert.Equal(CommandLine.PerFileWithListKey, CommandLine.ValidationKey("{paths}", runPerFile: true));
        Assert.Equal(CommandLine.GroupWithoutListKey, CommandLine.ValidationKey("-i {path}", runPerFile: false));
    }


    // --- OutputNames --------------------------------------------------------

    [Fact]
    public void Output_LandsBesideTheSource() {
        Assert.Equal(@"D:\My Videos\clip one.mkv", OutputNames.Resolve("{name}.mkv", Clip, _ => false, NoNames));
        Assert.Equal(@"D:\My Videos\clip one_small.mp4", OutputNames.Resolve("{name}_small.{ext}", Clip, _ => false, NoNames));
    }

    [Fact]
    public void Output_GoesToTheFolderGiven_AndIsUniqueThere() {
        var taken = Folder(@"E:\done\clip one.mkv");

        Assert.Equal(@"E:\done\clip one.mkv",
            OutputNames.Resolve("{name}.mkv", Clip, _ => false, NoNames, folder: @"E:\done"));
        Assert.Equal(@"E:\done\clip one (1).mkv",
            OutputNames.Resolve("{name}.mkv", Clip, taken.Contains, _ => taken.Select(Path.GetFileName)!, folder: @"E:\done"));
    }

    [Fact]
    public void Output_NeverOverwrites_TheSourceIncluded() {
        var taken = Folder(Clip, @"D:\My Videos\clip one (1).mp4");

        Assert.Equal(@"D:\My Videos\clip one (2).mp4", Resolve("{name}.{ext}", taken));
    }

    [Fact]
    public void Output_TakesTheNumberAfterTheHighest_NotTheFirstGap() {
        var taken = Folder(@"D:\My Videos\clip one.mkv", @"D:\My Videos\clip one (3).mkv", @"D:\My Videos\CLIP ONE (4).MKV");

        Assert.Equal(@"D:\My Videos\clip one (5).mkv", Resolve("{name}.mkv", taken));
    }

    [Fact]
    public void Output_CountsOnlyItsOwnNumberedNames() {
        var taken = Folder(
            @"D:\My Videos\clip one.mkv",
            @"D:\My Videos\clip one 7.mkv",
            @"D:\My Videos\clip one (x).mkv",
            @"D:\My Videos\clip one (8).mp4",
            @"D:\My Videos\other (9).mkv",
            @"D:\My Videos\clip one (2) copy.mkv");

        Assert.Equal(@"D:\My Videos\clip one (1).mkv", Resolve("{name}.mkv", taken));
    }

    [Fact]
    public void Output_NameWithPatternCharacters_IsMatchedLiterally() {
        const string source = @"C:\a+b [1].mov";
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\a+b [1].mp4", @"C:\a+b [1] (2).mp4", @"C:\aab [1] (6).mp4" };

        Assert.Equal(@"C:\a+b [1] (3).mp4",
            OutputNames.Resolve("{name}.mp4", source, taken.Contains, _ => taken.Select(Path.GetFileName)!));
    }

    [Fact]
    public void Output_StillChecksTheNumberItPicked() {
        // A folder that appeared after the listing was read.
        var listed = Folder(@"D:\My Videos\clip one.mkv", @"D:\My Videos\clip one (1).mkv");
        var taken = new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase) { @"D:\My Videos\clip one (2).mkv" };

        Assert.Equal(@"D:\My Videos\clip one (3).mkv",
            OutputNames.Resolve("{name}.mkv", Clip, taken.Contains, _ => listed.Select(Path.GetFileName)!));
    }


    private static IEnumerable<string> NoNames(string folder) {
        return Array.Empty<string>();
    }

    private static HashSet<string> Folder(params string[] paths) {
        return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
    }

    private static string Resolve(string template, HashSet<string> taken) {
        return OutputNames.Resolve(template, Clip, taken.Contains, _ => taken.Select(Path.GetFileName)!);
    }
}
