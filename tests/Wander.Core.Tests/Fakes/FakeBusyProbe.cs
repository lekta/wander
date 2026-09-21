using Wander.Core.FileSystem;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// A probe that finds a path held for a given number of looks, then free -
/// or held for good when the count is <see cref="int.MaxValue"/>. Every
/// look is counted in <see cref="Looks"/>.
/// </summary>
internal sealed class FakeBusyProbe : IFileBusyProbe {
    /// <summary>How many more looks find the path held.</summary>
    public Dictionary<string, int> HeldFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Looks { get; } = new();


    public bool IsBusy(string path) {
        Looks.Add(path);
        if (!HeldFor.TryGetValue(path, out int left) || left <= 0) {
            return false;
        }
        if (left != int.MaxValue) {
            HeldFor[path] = left - 1;
        }

        return true;
    }
}
