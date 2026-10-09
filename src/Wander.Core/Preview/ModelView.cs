namespace Wander.Core.Preview;

/// <summary>
/// How a model is turned in the preview, in degrees: about its vertical
/// axis (<paramref name="Spin"/>), then tilted toward the viewer
/// (<paramref name="Tilt"/>), both about the centre of its box - the
/// pane's two rotations, in the pane's order.
/// </summary>
public readonly record struct ModelView(double Spin, double Tilt) {
    /// <summary>
    /// What a model opens on, and its tile shows, until it is turned: a
    /// third of the way round, so a side shows beside the front - seen
    /// dead-on, a cabinet or a chainsaw was a flat silhouette (2026-10-09).
    /// The camera already stands a little above.
    /// </summary>
    public static ModelView Default => new(30, 0);

    /// <summary>The spin brought into (-180, 180], the tilt into the pane's limits.</summary>
    public ModelView Normalized() {
        double spin = Spin % 360;
        if (spin > 180) {
            spin -= 360;
        } else if (spin <= -180) {
            spin += 360;
        }

        return new ModelView(spin, Math.Clamp(Tilt, -89, 89));
    }
}


/// <summary>
/// The view each model was last turned to in the preview, by path: the
/// pane opens it so again, and its tile is drawn so. A file never turned
/// has none, and is shown at <see cref="ModelView.Default"/>.
/// </summary>
public interface IModelViews {
    ModelView? Get(string path);

    void Set(string path, ModelView view);
}
