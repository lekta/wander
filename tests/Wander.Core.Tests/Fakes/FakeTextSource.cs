using Wander.Core.Localization;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// A string table a test controls. Passed explicitly rather than registered
/// in <c>ServiceLocator</c>: the locator is process-wide state, and a test
/// class that mutates it races every other class running beside it.
/// Unknown keys come back as themselves, matching the real fallback.
/// </summary>
internal sealed class FakeTextSource : ITextSource {
    private readonly IReadOnlyDictionary<string, string> _values;


    public FakeTextSource(IReadOnlyDictionary<string, string> values) {
        _values = values;
    }


    public string Get(string key) {
        return _values.TryGetValue(key, out string? value) ? value : key;
    }
}
