namespace Wander.App.ViewModels;

/// <summary>
/// One line of the folder census in the preview pane: a file type, how much
/// of the folder it is, and the width of its bar. The bar width is a plain
/// number rather than a converter because the scale is relative to the
/// biggest bucket, which only the controller that built the list knows.
/// </summary>
public sealed record FolderTypeRow(string Extension, string CountText, string SizeText, double BarWidth);
