namespace Wander.Core.Folders;

/// <summary>Why a folder is drawn the way it is - the word in the View menu's hint.</summary>
public enum ViewReason {
    /// <summary>The user pinned this view to this folder.</summary>
    Pinned,

    /// <summary>The folder is mostly pictures and the automatic gallery is on.</summary>
    Pictures,

    /// <summary>Nothing said otherwise: the default view from the settings.</summary>
    Default,
}


/// <summary>The view for a folder and the reason it was picked.</summary>
public readonly record struct ViewDecision(ViewMode Mode, ViewReason Reason);


/// <summary>
/// Which view a folder gets on arrival. One rule, in this order: a pin the
/// user put on the folder wins; otherwise the gallery, when the folder is
/// mostly pictures and the automatic gallery is on; otherwise the default
/// from the settings. Only on arrival - F5 and a re-read after an operation
/// keep whatever is on screen, and the caller knows the difference.
///
/// <para>
/// Facts in, decision out: the caller supplies the pin (from
/// <see cref="FolderSettingsBook"/>), the settings and a predicate for "is
/// this a folder of pictures". The predicate is a function rather than a
/// value because it costs a pass over the listing, and a pinned folder
/// should not pay for it.
/// </para>
/// </summary>
public static class ViewChoice {
    /// <param name="pinned">The view pinned to the folder, or null when it has none.</param>
    /// <param name="autoGallery">The setting: may the gallery switch itself on at all.</param>
    /// <param name="inRecycleBin">
    /// The Recycle Bin is a list of things to decide about, not a folder to
    /// look at; it never turns into a gallery whatever it holds.
    /// </param>
    /// <param name="looksLikePictures">Is the listing mostly pictures - evaluated only when it can matter.</param>
    /// <param name="defaultMode">The view for folders nothing else has an opinion on.</param>
    public static ViewDecision Decide(
        ViewMode? pinned, bool autoGallery, bool inRecycleBin, Func<bool> looksLikePictures, ViewMode defaultMode) {
        if (pinned is { } mode) {
            return new ViewDecision(mode, ViewReason.Pinned);
        }
        if (autoGallery && !inRecycleBin && looksLikePictures()) {
            return new ViewDecision(ViewMode.Gallery, ViewReason.Pictures);
        }

        return new ViewDecision(defaultMode, ViewReason.Default);
    }
}
