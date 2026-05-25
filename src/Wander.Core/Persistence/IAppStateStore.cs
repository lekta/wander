namespace Wander.Core.Persistence;

public interface IAppStateStore {
    AppState Load();
    void Save(AppState state);
}
