namespace Wander.Core.Persistence;

public sealed record AppState {
    public string? LastPath { get; init; }
    public string? ViewMode { get; init; }
}
