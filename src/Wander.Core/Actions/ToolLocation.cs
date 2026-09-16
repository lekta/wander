namespace Wander.Core.Actions;

/// <summary>A program the user pointed Wander at by hand, on the settings page. Stored in <c>state.json</c>.</summary>
public sealed record ToolPath {
    /// <summary>The program's name, as <see cref="CustomAction.RequiredTool"/> gives it.</summary>
    public string Tool { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}


public enum ToolSource {
    /// <summary>On <c>PATH</c> or in an installer's folder.</summary>
    Found,

    /// <summary>Where the user pointed it.</summary>
    Specified,

    /// <summary>Nowhere, and the user gave no path.</summary>
    Missing,

    /// <summary>The user gave a path and there is no file there.</summary>
    SpecifiedMissing,
}


/// <summary>Where a program is, and how that was learned.</summary>
/// <param name="Path">
/// The executable for <see cref="ToolSource.Found"/> and
/// <see cref="ToolSource.Specified"/>; the path the user gave for
/// <see cref="ToolSource.SpecifiedMissing"/>, so the page can say which;
/// null otherwise.
/// </param>
public sealed record ToolLocation(string Tool, ToolSource Source, string? Path) {
    public bool IsAvailable => Source is ToolSource.Found or ToolSource.Specified;

    /// <summary>What to start; null when there is nothing to start.</summary>
    public string? Executable => IsAvailable ? Path : null;
}
