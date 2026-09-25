using System.Collections.Immutable;
using Wander.Core.Panels;

namespace Wander.Core.Tests;

/// <summary>A panel drawn as a list (decision P3): what is shown, how deep, told apart.</summary>
public class PanelViewTests {
    [Fact]
    public void OpenRowsShowTheirLevels_ClosedOnesDoNot() {
        var panel = Panel().WithExpanded(@"C:\", true);

        var lines = PanelView.Rows(panel);

        Assert.Equal(new[] { @"C:\", @"C:\A", @"C:\E" }, lines.Select(l => l.Path));
        Assert.Equal(new[] { 0, 1, 1 }, lines.Select(l => l.Depth));
        Assert.True(lines[0].IsExpanded);
        Assert.False(lines[1].IsExpanded);
        Assert.Equal(@"C:\", lines[1].Parent);
    }

    /// <summary>A row open with no subfolders left stays open: the opening is the user's, not the folder's.</summary>
    [Fact]
    public void AnOpenRowWithNoSubfolders_StaysOpen() {
        var panel = Panel().WithExpanded(@"C:\", true).WithExpanded(@"C:\E", true);

        Assert.True(PanelView.Rows(panel).Single(l => l.Path == @"C:\E").IsExpanded);
    }

    /// <summary>A shell row and a bookmark whose folder is gone are never drawn open, whatever was remembered of them.</summary>
    [Fact]
    public void ALeaf_IsNeverDrawnOpen() {
        var bin = new PanelRow("shell:RecycleBinFolder", "Bin", PanelRowKind.Shell) { Role = PanelRowRole.BuiltInBookmark };
        var gone = new PanelRow(@"D:\Gone", "Gone", PanelRowKind.Folder) { IsMissing = true, Role = PanelRowRole.OwnBookmark };
        var panel = PanelState.Empty
            .WithLevel(PanelState.TopKey, new PanelLevel(LevelState.Loaded, ImmutableArray.Create(bin, gone), 1))
            .WithExpanded(bin.Path, true)
            .WithExpanded(gone.Path, true);

        Assert.All(PanelView.Rows(panel), l => Assert.False(l.IsExpanded));
    }

    /// <summary>The same folder twice - a bookmark inside another - is two lines, told apart by their keys.</summary>
    [Fact]
    public void TheSameFolderTwice_IsTwoLinesWithTheirOwnKeys() {
        var photos = new PanelRow(@"D:\Photos", "Photos", PanelRowKind.Folder) { Role = PanelRowRole.OwnBookmark };
        var year = new PanelRow(@"D:\Photos\2026", "2026", PanelRowKind.Folder) { Role = PanelRowRole.OwnBookmark };
        var panel = PanelState.Empty
            .WithLevel(PanelState.TopKey, new PanelLevel(LevelState.Loaded, ImmutableArray.Create(photos, year), 1))
            .WithLevel(@"D:\Photos", new PanelLevel(LevelState.Loaded, ImmutableArray.Create(year with { Role = PanelRowRole.Normal }), 2))
            .WithExpanded(@"D:\Photos", true);

        var lines = PanelView.Rows(panel);

        Assert.Equal(3, lines.Length);
        Assert.Equal(2, lines.Count(l => l.Path == @"D:\Photos\2026"));
        Assert.Equal(lines.Length, lines.Select(l => l.Key).Distinct().Count());
    }

    [Fact]
    public void NearestIndexOf_StandsInForAHiddenRow() {
        var lines = PanelView.Rows(Panel().WithExpanded(@"C:\", true));

        Assert.Equal(1, PanelView.NearestIndexOf(lines, @"C:\A\B\C"));
        Assert.Equal(-1, PanelView.NearestIndexOf(lines, @"Z:\x"));
        Assert.Equal(-1, PanelView.IndexOf(lines, @"C:\A\B\C"));
    }


    private static PanelState Panel() {
        var drive = new PanelRow(@"C:\", @"C:\", PanelRowKind.Drive);
        var a = new PanelRow(@"C:\A", "A", PanelRowKind.Folder) { Children = ChildrenKnown.Yes };
        var e = new PanelRow(@"C:\E", "E", PanelRowKind.Folder) { Children = ChildrenKnown.No };

        return PanelState.Empty
            .WithLevel(PanelState.TopKey, new PanelLevel(LevelState.Loaded, ImmutableArray.Create(drive), 1))
            .WithLevel(@"C:\", new PanelLevel(LevelState.Loaded, ImmutableArray.Create(a, e), 2));
    }
}
