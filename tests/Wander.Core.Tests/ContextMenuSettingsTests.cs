using Wander.Core.Menu;
using Wander.Core.Persistence;

namespace Wander.Core.Tests;

public class ContextMenuSettingsTests {

    // --- Name normalisation ----------------------------------------------

    [Theory]
    [InlineData("&7-Zip", "7-Zip")]
    [InlineData("7-Zip", "7-Zip")]
    [InlineData("Открыть с помощью...", "Открыть с помощью")]
    [InlineData("Открыть с помощью…", "Открыть с помощью")]
    [InlineData("  Send &to  ", "Send to")]
    [InlineData("", "")]
    public void NormalizeName_StripsWin32Decoration(string header, string expected) {
        Assert.Equal(expected, ContextMenuSettings.NormalizeName(header));
    }

    [Fact]
    public void IsBlocked_IgnoresDecorationOnBothSides() {
        var settings = ContextMenuSettings.From(new AppSettings {
            BlockedShellExtensions = new[] { "&7-Zip" },
        });

        Assert.True(settings.IsBlocked("7-Zip"));
        Assert.True(settings.IsBlocked("7-Zip..."));
        Assert.False(settings.IsBlocked("TortoiseGit"));
    }


    // --- Projection from AppSettings --------------------------------------

    [Fact]
    public void From_ReadsHiddenIdsByName() {
        var settings = ContextMenuSettings.From(new AppSettings {
            HiddenContextMenuItems = new[] { "Copy", "FileSubmenu" },
        });

        Assert.True(settings.IsHidden(MenuCommandId.Copy));
        Assert.True(settings.IsHidden(MenuCommandId.FileSubmenu));
        Assert.False(settings.IsHidden(MenuCommandId.Cut));
    }

    [Fact]
    public void From_NeverTreatsNoneAsHidden() {
        // Every shell entry carries Id = None; hiding it would empty the
        // whole third-party block by accident.
        var settings = ContextMenuSettings.From(new AppSettings {
            HiddenContextMenuItems = new[] { "None" },
        });

        Assert.False(settings.IsHidden(MenuCommandId.None));
        Assert.Empty(settings.HiddenItems);
    }

    [Fact]
    public void From_DefaultsToNothingHiddenAndExtensionsOn() {
        var settings = ContextMenuSettings.From(new AppSettings());

        Assert.True(settings.ShellExtensionsEnabled);
        Assert.Empty(settings.HiddenItems);
        Assert.Empty(settings.BlockedShellExtensions);
    }


    // --- Remembered extension names ---------------------------------------

    [Fact]
    public void TrimKnownExtensions_NormalisesAndDeduplicates() {
        var trimmed = ContextMenuSettings.TrimKnownExtensions(
            new[] { "&7-Zip", "7-Zip", "7-Zip...", "", "   ", "TortoiseGit" },
            Array.Empty<string>());

        Assert.Equal(new[] { "7-Zip", "TortoiseGit" }, trimmed);
    }

    [Fact]
    public void TrimKnownExtensions_LeavesAShortListAlone() {
        var names = new[] { "7-Zip", "TortoiseGit", "Notepad++" };

        Assert.Equal(names, ContextMenuSettings.TrimKnownExtensions(names, Array.Empty<string>()));
    }

    [Fact]
    public void TrimKnownExtensions_DropsTheOldestOverTheCap() {
        // Handlers that put a branch or file name in their label would grow
        // this list forever otherwise.
        var names = Enumerable.Range(0, ContextMenuSettings.MaxKnownShellExtensions + 10)
            .Select(i => $"handler {i}")
            .ToArray();

        var trimmed = ContextMenuSettings.TrimKnownExtensions(names, Array.Empty<string>());

        Assert.Equal(ContextMenuSettings.MaxKnownShellExtensions, trimmed.Count);
        Assert.DoesNotContain("handler 0", trimmed);
        Assert.Contains(names[^1], trimmed);
    }

    [Fact]
    public void TrimKnownExtensions_NeverDropsABlockedName() {
        // Forgetting a blocked name would silently switch that handler back
        // on the next time it shows up.
        var names = Enumerable.Range(0, ContextMenuSettings.MaxKnownShellExtensions + 10)
            .Select(i => $"handler {i}")
            .ToArray();

        var trimmed = ContextMenuSettings.TrimKnownExtensions(names, new[] { "handler 0", "handler 1" });

        Assert.Contains("handler 0", trimmed);
        Assert.Contains("handler 1", trimmed);
        Assert.Equal(ContextMenuSettings.MaxKnownShellExtensions, trimmed.Count);
    }

    [Fact]
    public void TrimKnownExtensions_KeepsEveryBlockedNameEvenPastTheCap() {
        var names = Enumerable.Range(0, ContextMenuSettings.MaxKnownShellExtensions + 10)
            .Select(i => $"handler {i}")
            .ToArray();

        var trimmed = ContextMenuSettings.TrimKnownExtensions(names, names);

        Assert.Equal(names.Length, trimmed.Count);
    }

    [Fact]
    public void TrimKnownExtensions_SurvivesAnAppSettingsRoundTrip() {
        var stored = new AppSettings {
            KnownShellExtensions = new[] { "&7-Zip", "TortoiseGit" },
            BlockedShellExtensions = new[] { "7-Zip..." },
        };

        var settings = ContextMenuSettings.From(stored);
        var trimmed = ContextMenuSettings.TrimKnownExtensions(
            stored.KnownShellExtensions, stored.BlockedShellExtensions);

        Assert.Equal(new[] { "7-Zip", "TortoiseGit" }, trimmed);
        Assert.True(settings.IsBlocked(trimmed[0]));
        Assert.False(settings.IsBlocked(trimmed[1]));
    }
}
