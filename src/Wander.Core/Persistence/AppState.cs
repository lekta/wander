namespace Wander.Core.Persistence;

public sealed record AppState {
    public string? LastPath { get; init; }
    public string? ViewMode { get; init; }
    public IReadOnlyList<string> ExpandedPaths { get; init; } = Array.Empty<string>();
    public bool IsPreviewVisible { get; init; }
    public double PreviewWidth { get; init; } = 280;

    // --- Window geometry ----------------------------------------------
    // Stored when the user closes Wander. Nullable so a fresh install
    // (or rolled-back schema) falls back to the XAML defaults rather
    // than to (0, 0, 0, 0). Width/Height are remembered even when the
    // window was Maximized at close — that way restoring it to Normal
    // lands at the same size it had before being maximized.
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
    public bool WindowMaximized { get; init; }
}
