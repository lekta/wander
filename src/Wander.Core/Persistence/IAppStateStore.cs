namespace Wander.Core.Persistence;

public interface IAppStateStore {
    /// <summary>
    /// True while this instance must not write: another instance owns the
    /// file (a Debug session started with <c>--yield</c> beside the
    /// installed copy). <see cref="Save"/> then does nothing, and the
    /// window says so in its title. Sticky - once seen, it stays for the
    /// session, so what this instance saved before cannot land on top of
    /// what the other one wrote in between.
    /// </summary>
    bool IsReadOnly { get; }

    AppState Load();
    void Save(AppState state);
}
