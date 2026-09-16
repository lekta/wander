using Wander.Core.Actions;
using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class ActionCatalogTests {
    private const string Ffmpeg = @"C:\tools\ffmpeg.exe";

    private static readonly CustomAction _presetA = new() {
        Id = "preset:a", TitleKey = "ActionPresetA", Program = "ffmpeg", RequiredTool = "ffmpeg",
        Arguments = "-i {path} {out}", Output = "{name}.mp4", IsPreset = true, Category = ActionCategory.Convert,
    };

    private static readonly CustomAction _presetB = _presetA with { Id = "preset:b", TitleKey = "ActionPresetB" };

    private static readonly CustomAction[] _presets = { _presetA, _presetB };

    private static readonly CustomAction _own = new() { Id = "own", Title = "Notepad", Program = @"C:\np.exe" };


    [Fact]
    public void Merge_WithNothingStored_IsThePresets() {
        var catalog = ActionCatalog.Merge(_presets, Array.Empty<CustomAction>());

        Assert.Equal(_presets, catalog);
    }

    [Fact]
    public void Merge_StoredPreset_BringsOnlyItsSwitchAndProgram_TheRestFollowsTheCode() {
        // What an older build stored whole, or a hand-edited state.json:
        // the command line in it is stale and must not shadow the code's.
        var stored = _presetB with { Enabled = false, Program = Ffmpeg, Arguments = "stale", Output = "stale" };

        var catalog = ActionCatalog.Merge(_presets, new[] { stored });

        Assert.Equal(new[] { _presetA, _presetB with { Enabled = false, Program = Ffmpeg } }, catalog);
    }

    [Fact]
    public void Merge_StoredOverride_WithoutAProgram_KeepsTheShippedOne() {
        var stored = new CustomAction { Id = "preset:b", IsPreset = true, Enabled = false };

        var catalog = ActionCatalog.Merge(_presets, new[] { stored });

        Assert.Equal(_presetB with { Enabled = false }, catalog[1]);
    }

    [Fact]
    public void Merge_OwnRows_FollowThePresets_InStoredOrder() {
        var second = _own with { Id = "own2", Title = "Second" };

        var catalog = ActionCatalog.Merge(_presets, new[] { second, _own });

        Assert.Equal(new[] { _presetA, _presetB, second, _own }, catalog);
    }

    [Fact]
    public void Merge_DropsAPresetTheCodeNoLongerShips_AndDuplicateOwnIds() {
        var retired = _presetA with { Id = "preset:gone" };

        var catalog = ActionCatalog.Merge(_presets, new[] { retired, _own, _own with { Title = "Again" } });

        Assert.Equal(new[] { _presetA, _presetB, _own }, catalog);
    }

    [Fact]
    public void Merge_GivesAnIdToARowWithout() {
        var catalog = ActionCatalog.Merge(_presets, new[] { _own with { Id = "" } });

        Assert.NotEqual(string.Empty, catalog[2].Id);
        Assert.Equal("Notepad", catalog[2].Title);
    }

    [Fact]
    public void ToStored_KeepsOnlyTouchedPresets_AsTheirSwitchAndProgram_AndOwnRows() {
        var touched = _presetB with { Enabled = false };
        var pointed = _presetA with { Program = Ffmpeg };

        var stored = ActionCatalog.ToStored(_presets, new[] { pointed, touched, _own });

        Assert.Equal(new[] {
            new CustomAction { Id = "preset:a", IsPreset = true, Enabled = true, Program = Ffmpeg },
            new CustomAction { Id = "preset:b", IsPreset = true, Enabled = false },
            _own,
        }, stored);
    }

    [Fact]
    public void ToStored_ThenMerge_GivesTheSameCatalog() {
        var catalog = new[] { _presetA with { Program = Ffmpeg }, _presetB, _own };

        var again = ActionCatalog.Merge(_presets, ActionCatalog.ToStored(_presets, catalog));

        Assert.Equal(catalog, again);
    }

    [Fact]
    public void CopyOf_IsTheUsersOwn_WithTheSameCommand() {
        var copy = ActionCatalog.CopyOf(_presetA, "Mine");

        Assert.False(copy.IsPreset);
        Assert.Equal("Mine", copy.DisplayTitle);
        Assert.Equal(string.Empty, copy.TitleKey);
        Assert.NotEqual(_presetA.Id, copy.Id);
        Assert.Equal(_presetA.Arguments, copy.Arguments);
        Assert.Equal(_presetA.RequiredTool, copy.RequiredTool);
        Assert.Equal(ActionCategory.Convert, copy.Category);
    }

    [Fact]
    public void NewAction_IsValidForItsMode() {
        var row = ActionCatalog.NewAction("New");

        Assert.NotEqual(string.Empty, row.Id);
        Assert.Null(CommandLine.ValidationKey(row.Arguments, row.RunPerFile));
    }

    // --- Programs ---------------------------------------------------------

    [Fact]
    public void ToolNames_AreTheListedOnes_ThenWhatARowNeeds() {
        var magick = _own with { RequiredTool = "magick" };

        var names = ActionCatalog.ToolNames(new[] { _presetA, magick, magick with { RequiredTool = "FFMPEG" } });

        Assert.Equal(new[] { "ffmpeg", "soffice", "pandoc", "magick" }, names);
    }

    [Fact]
    public void LocateTools_WithNothingGiven_AsksTheLocator() {
        var tools = ActionCatalog.LocateTools(
            new[] { _presetA }, Array.Empty<ToolPath>(), t => t == "ffmpeg" ? Ffmpeg : null, _ => true);

        Assert.Equal(new ToolLocation("ffmpeg", ToolSource.Found, Ffmpeg), tools["FFMPEG"]);
        Assert.Equal(new ToolLocation("pandoc", ToolSource.Missing, null), tools["pandoc"]);
        Assert.Equal(3, tools.Count);
    }

    [Fact]
    public void LocateTools_APathTheUserGave_Wins_AndIsNotReplacedWhenGone() {
        const string mine = @"D:\my\ffmpeg.exe";
        var given = new[] { new ToolPath { Tool = "FFmpeg", Path = " " + mine + " " } };

        var there = ActionCatalog.LocateTools(new[] { _presetA }, given, _ => Ffmpeg, p => p == mine);
        var gone = ActionCatalog.LocateTools(new[] { _presetA }, given, _ => Ffmpeg, _ => false);

        Assert.Equal(new ToolLocation("ffmpeg", ToolSource.Specified, mine), there["ffmpeg"]);
        Assert.Equal(mine, there["ffmpeg"].Executable);
        Assert.Equal(new ToolLocation("ffmpeg", ToolSource.SpecifiedMissing, mine), gone["ffmpeg"]);
        Assert.Null(gone["ffmpeg"].Executable);
        Assert.DoesNotContain("ffmpeg", ActionCatalog.Missing(there));
        Assert.Contains("ffmpeg", ActionCatalog.Missing(gone));
    }

    [Fact]
    public void WithToolPath_ReplacesOrForgets_OneProgram() {
        var paths = ActionCatalog.WithToolPath(Array.Empty<ToolPath>(), "ffmpeg", Ffmpeg);
        paths = ActionCatalog.WithToolPath(paths, "pandoc", @"C:\p\pandoc.exe");
        paths = ActionCatalog.WithToolPath(paths, "FFMPEG", @"D:\ffmpeg.exe");

        Assert.Equal(@"D:\ffmpeg.exe", ActionCatalog.SpecifiedPath(paths, "ffmpeg"));
        Assert.Equal(2, paths.Count);

        paths = ActionCatalog.WithToolPath(paths, "ffmpeg", "  ");

        Assert.Null(ActionCatalog.SpecifiedPath(paths, "ffmpeg"));
        Assert.Equal(@"C:\p\pandoc.exe", ActionCatalog.SpecifiedPath(paths, "pandoc"));
    }

    [Fact]
    public void NeedsTool_OnlyForARowWhoseToolIsNotAvailable() {
        var missing = Tools(new ToolLocation("ffmpeg", ToolSource.SpecifiedMissing, @"D:\gone.exe"));
        var found = Tools(new ToolLocation("ffmpeg", ToolSource.Found, Ffmpeg));

        Assert.True(ActionCatalog.NeedsTool(_presetA, missing));
        Assert.True(ActionCatalog.NeedsTool(_presetA, Tools()));
        Assert.False(ActionCatalog.NeedsTool(_presetA, found));
        Assert.False(ActionCatalog.NeedsTool(_own, missing));
    }

    [Fact]
    public void WithLocatedProgram_ReplacesOnlyTheToolsBareName() {
        var tools = Tools(new ToolLocation("ffmpeg", ToolSource.Specified, Ffmpeg));
        var own = _presetA with { Program = @"D:\other\ffmpeg.exe" };
        var exe = _presetA with { Program = "ffmpeg.exe" };

        Assert.Equal(Ffmpeg, ActionCatalog.WithLocatedProgram(_presetA, tools).Program);
        Assert.Equal(Ffmpeg, ActionCatalog.WithLocatedProgram(exe, tools).Program);
        Assert.Same(own, ActionCatalog.WithLocatedProgram(own, tools));
        Assert.Same(_own, ActionCatalog.WithLocatedProgram(_own, tools));
    }

    [Fact]
    public void WithLocatedProgram_LeavesTheNameWhenTheToolIsNotAvailable() {
        var tools = Tools(new ToolLocation("ffmpeg", ToolSource.SpecifiedMissing, @"D:\gone.exe"));

        Assert.Equal("ffmpeg", ActionCatalog.WithLocatedProgram(_presetA, tools).Program);
    }


    // --- The shipped set -------------------------------------------------

    [Fact]
    public void Presets_HaveUniquePresetIds_AndTitleKeys() {
        var all = ActionPresets.All;

        Assert.Equal(all.Count, all.Select(p => p.Id).Distinct().Count());
        Assert.All(all, p => Assert.StartsWith("preset:", p.Id));
        Assert.All(all, p => Assert.StartsWith("ActionPreset", p.TitleKey));
        Assert.All(all, p => Assert.Equal(string.Empty, p.Title));
        Assert.All(all, p => Assert.True(p.IsPreset));
        Assert.All(all, p => Assert.Equal(ActionCategory.Convert, p.Category));
    }

    [Fact]
    public void Presets_AreRunnableAsWritten() {
        foreach (var preset in ActionPresets.All) {
            if (preset.Kind == ActionKind.Builtin) {
                Assert.Equal(ActionPresets.ImageConvert, preset.Program);
                Assert.Equal(string.Empty, preset.RequiredTool);
                Assert.NotEqual(string.Empty, preset.Output);
                continue;
            }

            Assert.Null(CommandLine.ValidationKey(preset.Arguments, preset.RunPerFile));
            Assert.Equal(preset.RequiredTool, preset.Program);
            Assert.NotNull(ActionPresets.KnownTool(preset.RequiredTool));
            // A declared output is one the command writes to.
            Assert.Equal(preset.Output.Length > 0, CommandLine.Uses(preset.Arguments, "{out}"));
        }
    }

    [Fact]
    public void Presets_OnlyLibreOffice_LeavesItsOutputUndeclared() {
        var undeclared = ActionPresets.All.Where(p => p.Output.Length == 0).ToArray();

        Assert.Single(undeclared);
        Assert.Equal(ActionPresets.LibreOffice, undeclared[0].RequiredTool);
    }

    [Fact]
    public void Presets_MergedWithNothing_AreNotStored() {
        var catalog = ActionCatalog.Merge(ActionPresets.All, Array.Empty<CustomAction>());

        Assert.Empty(ActionCatalog.ToStored(ActionPresets.All, catalog));
    }

    [Fact]
    public void KnownTool_ListsTheThreePrograms_CaseInsensitively() {
        Assert.Equal("Gyan.FFmpeg", ActionPresets.KnownTool("FFmpeg")?.WingetId);
        Assert.Equal("TheDocumentFoundation.LibreOffice", ActionPresets.KnownTool("soffice")?.WingetId);
        Assert.Equal("JohnMacFarlane.Pandoc", ActionPresets.KnownTool("pandoc")?.WingetId);
        Assert.Null(ActionPresets.KnownTool("magick"));
    }

    [Theory]
    [InlineData("IMG_1.CR3", true)]
    [InlineData("a.nef", true)]
    [InlineData("a.jpg", false)]
    public void RawPreview_IsOfferedForRawFilesOnly(string name, bool expected) {
        var preset = ActionPresets.All.Single(p => p.Id == "preset:raw-preview");
        var entry = new FileSystemEntry(name, @"C:\" + name, EntryKind.File, 1, DateTime.MinValue, false, false, false, false);

        Assert.Equal(expected, preset.Types.Matches(entry));
        Assert.True(ImageConvertOptions.Parse(preset.Arguments).FromPreview);
    }


    private static IReadOnlyDictionary<string, ToolLocation> Tools(params ToolLocation[] locations) {
        return locations.ToDictionary(l => l.Tool, StringComparer.OrdinalIgnoreCase);
    }
}
