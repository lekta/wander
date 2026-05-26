namespace Wander.Core.Persistence;

public sealed record AppState {
    public string? LastPath { get; init; }
    public string? ViewMode { get; init; }
    public IReadOnlyList<string> ExpandedPaths { get; init; } = Array.Empty<string>();
    public bool IsPreviewVisible { get; init; }
    public double PreviewWidth { get; init; } = 280;
}
