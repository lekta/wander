using Wander.Core.Persistence;
using Wander.Core.Pins;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// <see cref="IPinStore"/> in memory: what was saved last is what the next
/// load returns; <see cref="Saves"/> counts the writes. Read-only drops them,
/// as a yielding instance does.
/// </summary>
internal sealed class FakePinStore : IPinStore {
    public IReadOnlyList<PinFolder> Saved { get; private set; } = Array.Empty<PinFolder>();

    public int Saves { get; private set; }

    public bool IsReadOnly { get; set; }


    public IReadOnlyList<PinFolder> Load() {
        return Saved;
    }

    public void Save(IReadOnlyList<PinFolder> folders) {
        if (IsReadOnly) {
            return;
        }

        Saved = folders;
        Saves++;
    }
}
