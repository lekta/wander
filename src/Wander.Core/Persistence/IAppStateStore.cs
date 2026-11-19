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

    /// <summary>
    /// How the last <see cref="Load"/> went: the defaults it gave are a first
    /// start only when the file was <see cref="StoreReadOutcome.Missing"/>
    /// (audit 2026-11-18, L8).
    /// </summary>
    StoreReadOutcome LastLoad { get; }

    AppState Load();

    /// <returns>True when the file was written; false when this instance must not write it, or the write failed.</returns>
    bool Save(AppState state);
}
