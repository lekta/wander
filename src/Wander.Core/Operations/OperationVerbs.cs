namespace Wander.Core.Operations;

/// <summary>
/// What an operation is called, as a key into the app's string table rather
/// than a word. Core starts the operations and has no string table of its
/// own (see <c>ITextSource</c>), so the verb travels as a key and the app
/// turns it into "Копирование" on the way to the screen.
/// </summary>
public static class OperationVerbs {
    public const string Copy = "ProgressCopying";
    public const string Move = "ProgressMoving";
    public const string Recycle = "ProgressRecycling";
    public const string DeletePermanently = "ProgressDeleting";
    public const string Extract = "ProgressExtracting";
}
