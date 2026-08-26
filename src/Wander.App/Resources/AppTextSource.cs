using Wander.Core.Localization;

namespace Wander.App.Resources;

/// <summary>
/// Lets <c>Wander.Core</c> reach the app's string table without referencing
/// it. Registered in <c>App.OnStartup</c>; the handful of strings Core
/// produces for the user — context-menu labels, the reason a drop is
/// refused — resolve through here.
/// </summary>
public sealed class AppTextSource : ITextSource {
    public string Get(string key) {
        return Strings.Get(key);
    }
}
