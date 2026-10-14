using Wander.Core.Preview;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// A Windows with every Store extension but those in <see cref="Lacks"/>.
/// Every question is counted in <see cref="Asked"/>.
/// </summary>
internal sealed class FakeCodecProbe : ICodecProbe {
    public HashSet<MissingCodec> Lacks { get; } = new();

    public List<MissingCodec> Asked { get; } = new();


    public bool Has(MissingCodec codec) {
        Asked.Add(codec);

        return !Lacks.Contains(codec);
    }
}
