namespace Wander.Core.Companions;

/// <summary>
/// What one click on a star or a swatch means for everything it is about
/// to change. Clicking the value that is already set takes it away again -
/// otherwise a mis-click could never be undone except through Ctrl+Z - and
/// with several files under the click the question becomes "is it already
/// set on all of them". It is: clear them all. It is not: set it on all of
/// them, the ones that already had it included, so one gesture leaves the
/// whole selection agreeing.
/// </summary>
public static class RatingToggle {
    /// <summary>
    /// The value to write. <paramref name="clicked"/> is the star or the
    /// swatch, 1...5; <paramref name="current"/> is what each target holds
    /// now, null for "nothing recorded". Zero comes back only when every
    /// target already shows the clicked value.
    /// </summary>
    public static int Resolve(int clicked, IEnumerable<int?> current) {
        if (clicked <= 0) {
            return 0;
        }

        bool any = false;
        foreach (int? value in current) {
            any = true;
            if ((value ?? 0) != clicked) {
                return clicked;
            }
        }

        return any ? 0 : clicked;
    }
}
