using System.Windows.Media;

namespace Wander.App.ViewModels;

/// <summary>
/// One line of an archive's listing in the preview pane: the icon its
/// extension gets, the name, and the size for a file (blank for a folder -
/// the shell has no size to give for one inside an archive).
///
/// <para>
/// The icon is carried rather than bound by path, the way the file list
/// does it: asking the shell about a path inside an archive costs a call
/// into it, and the listing is built off the UI thread anyway.
/// </para>
/// </summary>
public sealed record ArchiveEntryRow(ImageSource? Icon, string Name, string SizeText);
