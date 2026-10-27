namespace Wander.App.ViewModels;

/// <summary>
/// A row of the preview pane's stack of shapes (<c>SoundStack</c>): the
/// file, its length as text, and how much of the width its shape takes
/// against the longest of the stack - the whole width while the lengths are
/// still being read. The first row draws no hairline above it.
/// </summary>
public sealed record SoundRow(string Name, string FullPath, string Length, double Share, bool First);
