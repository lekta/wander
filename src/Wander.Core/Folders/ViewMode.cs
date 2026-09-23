namespace Wander.Core.Folders;

/// <summary>
/// How the file list draws a folder. Lives in Core because the choice is a
/// rule here (<see cref="ViewChoice"/>) and a stored fact (a folder's pin in
/// <see cref="FolderRecord"/>, the default in <c>AppSettings</c>); the WPF
/// views only render whichever one is picked.
/// </summary>
public enum ViewMode {
    Details,
    Tiles,
    LargeIcons,

    /// <summary>
    /// Big pictures on a plain background - the view for looking at
    /// photographs rather than at a folder. Switches itself on in folders
    /// that are mostly pictures unless the user has pinned a view there;
    /// see <see cref="ViewChoice"/>.
    /// </summary>
    Gallery,
}
