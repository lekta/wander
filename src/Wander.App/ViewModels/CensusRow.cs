using System.Windows.Input;

namespace Wander.App.ViewModels;

/// <summary>
/// One line of the folder census in the preview pane: a file type or a
/// subfolder, how much of the folder it is, and its share of the biggest
/// line - relative to that rather than to the total, which only the
/// controller that built the list knows. A click shows it in the list.
/// </summary>
/// <param name="Open">What a click does; null when there is nothing to show - files without an extension, the types of an archive.</param>
/// <param name="Hint">What the click will do.</param>
public sealed record CensusRow(string Name, string CountText, string SizeText, double Share, ICommand? Open, string? Hint) {
    public bool IsClickable => Open is not null;
}
