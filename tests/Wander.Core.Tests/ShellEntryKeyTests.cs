using Wander.Core.Menu;
using Wander.Core.Persistence;
using Wander.Core.Shell;

namespace Wander.Core.Tests;

/// <summary>
/// What a blocked menu row is remembered by. The whole point of the type is
/// that the obvious answer — the text on the row — is wrong often enough to
/// matter.
/// </summary>
public class ShellEntryKeyTests {

    [Fact]
    public void VerbWins_WhenTheHandlerPublishesOne() {
        Assert.Equal("git_shell", ShellEntryKey.For("git_shell", "Open Git Bash here"));
    }

    [Fact]
    public void LabelIsTheFallback_WhenThereIsNoVerb() {
        // 7-Zip's top-level popup publishes nothing; its label is all there
        // is, and it is stable because it is an application name.
        Assert.Equal("7-Zip", ShellEntryKey.For("", "&7-Zip"));
        Assert.Equal("7-Zip", ShellEntryKey.For(null, "7-Zip..."));
    }

    [Fact]
    public void TortoiseGitsBranchName_DoesNotMoveTheKey() {
        // The label carries the current branch — «Git Commit -> "master"...»
        // — so keying on it invents a new unknown entry on every checkout
        // while the block the user set quietly stops matching. The verb of
        // the same row has no branch in it.
        string onMaster = ShellEntryKey.For("Git Commit...", "Git Commit -> \"master\"...");
        string onFeature = ShellEntryKey.For("Git Commit...", "Git Commit -> \"feature/x\"...");

        Assert.Equal(onMaster, onFeature);
    }

    [Fact]
    public void BlockSetOnOneBranch_StillMatchesOnAnother() {
        var settings = ContextMenuSettings.From(new AppSettings {
            BlockedShellExtensions = new[] { "Git Commit..." },
        });

        Assert.True(settings.IsBlocked("Git Commit...", "Git Commit -> \"master\"..."));
        Assert.True(settings.IsBlocked("Git Commit...", "Git Commit -> \"release/2.0\"..."));
        Assert.False(settings.IsBlocked("Git Sync...", "Git Sync..."));
    }

    [Fact]
    public void BlocklistsWrittenBeforeVerbKeys_KeepWorking() {
        // Older builds stored the decorated label. No migration step: the
        // lookup checks both handles.
        var settings = ContextMenuSettings.From(new AppSettings {
            BlockedShellExtensions = new[] { "&7-Zip" },
        });

        Assert.True(settings.IsBlocked("", "7-Zip"));
        Assert.True(settings.IsBlocked(null, "&7-Zip"));
    }

    [Theory]
    [InlineData("&7-Zip", "7-Zip")]
    [InlineData("Открыть с помощью...", "Открыть с помощью")]
    [InlineData("Открыть с помощью…", "Открыть с помощью")]
    [InlineData("  Send &to  ", "Send to")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_StripsWin32Decoration(string? header, string expected) {
        Assert.Equal(expected, ShellEntryKey.Normalize(header));
    }
}
