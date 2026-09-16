using Wander.Core.Actions;
using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class ActionApplicabilityTests {
    private static readonly CustomAction _forVideo = new() {
        Id = "v", Title = "Encode", Types = new FileTypeSelector(FileTypeGroup.Video),
    };

    private static readonly CustomAction _forFolders = new() {
        Id = "f", Title = "Index", Types = new FileTypeSelector(FileTypeGroup.Folders),
    };

    private static readonly CustomAction _forAll = new() {
        Id = "a", Title = "Anything", Types = new FileTypeSelector(FileTypeGroup.All),
    };


    [Fact]
    public void AppliesWhenEverySelectedItemMatches() {
        Assert.Equal(ActionState.Applicable, State(_forVideo, File("a.mp4"), File("b.mkv")));
    }

    [Fact]
    public void OneOddItem_IsEnoughToWithholdTheAction() {
        // Partial matches are not explained or counted - see BACKLOG.
        Assert.Equal(ActionState.NotForSelection, State(_forVideo, File("a.mp4"), File("b.jpg")));
        Assert.Equal(ActionState.Applicable, State(_forAll, File("a.mp4"), File("b.jpg"), Dir("c")));
    }

    [Fact]
    public void NothingSelected_OnlyFolderActionsApply_ToTheFolderOnScreen() {
        Assert.Equal(ActionState.Applicable, State(_forFolders));
        Assert.Equal(ActionState.NeedsSelection, State(_forVideo));
        Assert.Equal(ActionState.NeedsSelection, State(_forAll));
        Assert.Equal(ActionState.NeedsSelection,
            ActionApplicability.For(_forFolders, Array.Empty<FileSystemEntry>(), hasFolder: false, _ => true));
    }

    [Fact]
    public void MissingTool_Counts_OnlyForAnActionThatWouldApply() {
        var needsFfmpeg = _forVideo with { RequiredTool = "ffmpeg" };
        Func<string, bool> noFfmpeg = tool => tool != "ffmpeg";

        Assert.Equal(ActionState.ToolMissing,
            ActionApplicability.For(needsFfmpeg, new[] { File("a.mp4") }, true, noFfmpeg));
        Assert.Equal(ActionState.Applicable,
            ActionApplicability.For(needsFfmpeg, new[] { File("a.mp4") }, true, _ => true));
        // Installing ffmpeg would not make it run on a picture, so there is
        // nothing to hint at.
        Assert.Equal(ActionState.NotForSelection,
            ActionApplicability.For(needsFfmpeg, new[] { File("a.jpg") }, true, noFfmpeg));
        Assert.Equal(ActionState.NeedsSelection,
            ActionApplicability.For(needsFfmpeg, Array.Empty<FileSystemEntry>(), true, noFfmpeg));
    }

    [Fact]
    public void MissingTool_OfAFolderAction_OnTheFolderOnScreen() {
        var needsTool = _forFolders with { RequiredTool = "tool" };

        Assert.Equal(ActionState.ToolMissing,
            ActionApplicability.For(needsTool, Array.Empty<FileSystemEntry>(), true, _ => false));
        Assert.Equal(ActionState.NeedsSelection,
            ActionApplicability.For(needsTool, Array.Empty<FileSystemEntry>(), hasFolder: false, _ => false));
    }


    private static ActionState State(CustomAction action, params FileSystemEntry[] selection) {
        return ActionApplicability.For(action, selection, hasFolder: true, _ => true);
    }

    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, @"C:\x\" + name, EntryKind.File, 1, DateTime.UnixEpoch, false, false, false, false);
    }

    private static FileSystemEntry Dir(string name) {
        return new FileSystemEntry(name, @"C:\x\" + name, EntryKind.Directory, null, DateTime.UnixEpoch, false, false, false, false);
    }
}
