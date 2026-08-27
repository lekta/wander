namespace Wander.App.DragPreview;

public enum DragAction {
    Move,
    Copy,
    Link,
    Forbidden,

    /// <summary>
    /// Something is being dragged but nothing has been aimed at yet — the
    /// cursor is over the list it came from. The plaque still says what is
    /// in hand, because letting go of a drag you cannot see is worse than
    /// any of the above; it just does not claim anything is wrong.
    /// </summary>
    None,
}
