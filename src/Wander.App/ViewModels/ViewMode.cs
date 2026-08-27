namespace Wander.App.ViewModels;

public enum ViewMode {
    Details,
    Tiles,
    LargeIcons,

    /// <summary>
    /// Big pictures on a plain background — the view for looking at
    /// photographs rather than at a folder. Switches itself on in folders
    /// that are mostly pictures unless the user has picked a view there by
    /// hand; see <c>MainViewModel.AutoSelectViewMode</c>.
    /// </summary>
    Gallery,
}
