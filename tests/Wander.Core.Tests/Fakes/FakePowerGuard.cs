using Wander.Core.Operations;

namespace Wander.Core.Tests.Fakes;

/// <summary>Counts the requests held - what the computer would be kept awake by.</summary>
internal sealed class FakePowerGuard : IPowerGuard {
    public int Held { get; private set; }

    public List<string> Reasons { get; } = new();


    public IDisposable KeepAwake(string reason) {
        Held++;
        Reasons.Add(reason);

        return new Hold(this);
    }


    private sealed class Hold : IDisposable {
        private FakePowerGuard? _owner;


        public Hold(FakePowerGuard owner) {
            _owner = owner;
        }


        public void Dispose() {
            if (_owner is not null) {
                _owner.Held--;
                _owner = null;
            }
        }
    }
}
