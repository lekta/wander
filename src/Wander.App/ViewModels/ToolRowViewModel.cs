using Wander.App.Resources;
using Wander.Core.Actions;

namespace Wander.App.ViewModels;

/// <summary>
/// One program on the "Программы" settings page: what it is for, where it
/// was found or pointed at, and the two buttons - point at a file by hand,
/// or go back to looking for it.
/// </summary>
public sealed class ToolRowViewModel : ObservableObject {
    private ToolLocation _location;


    public ToolRowViewModel(string tool) {
        Tool = tool;
        var known = ActionPresets.KnownTool(tool);
        Title = known?.Title ?? tool;
        Purpose = PurposeOf(tool);
        InstallHint = known is null ? string.Empty : string.Format(Strings.ToolsInstallHint, known.WingetId);
        _location = new ToolLocation(tool, ToolSource.Missing, null);
    }


    /// <summary>The name <see cref="CustomAction.RequiredTool"/> gives it.</summary>
    public string Tool { get; }

    public string Title { get; }

    /// <summary>What needs it; empty for a program only a user's row names.</summary>
    public string Purpose { get; }

    /// <summary>"winget install …" - shown while the program is missing.</summary>
    public string InstallHint { get; }

    public bool IsAvailable => _location.IsAvailable;

    /// <summary>The path was given by hand - "Сбросить" goes back to looking for it.</summary>
    public bool IsSpecified => _location.Source is ToolSource.Specified or ToolSource.SpecifiedMissing;

    public string StatusText => _location.Source switch {
        ToolSource.Found => Strings.ToolsStatusFound,
        ToolSource.Specified => Strings.ToolsStatusSpecified,
        ToolSource.SpecifiedMissing => Strings.ToolsStatusSpecifiedMissing,
        _ => Strings.ToolsStatusMissing,
    };

    /// <summary>The file, found or given; empty when there is none to show.</summary>
    public string PathText => _location.Path ?? string.Empty;


    public void SetLocation(ToolLocation location) {
        _location = location;
        Raise(nameof(IsAvailable));
        Raise(nameof(IsSpecified));
        Raise(nameof(StatusText));
        Raise(nameof(PathText));
    }


    private static string PurposeOf(string tool) {
        return tool.ToLowerInvariant() switch {
            ActionPresets.Ffmpeg => Strings.ToolsPurposeFfmpeg,
            ActionPresets.LibreOffice => Strings.ToolsPurposeLibreOffice,
            ActionPresets.Pandoc => Strings.ToolsPurposePandoc,
            _ => string.Empty,
        };
    }
}
