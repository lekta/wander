using Wander.Core.Localization;

namespace Wander.Core.Tests;

/// <summary>
/// What Core says when nobody answered about strings. `ITextSource` is one
/// of the services that really are optional — the string table lives in the
/// app layer, and Core runs without it in every test in this project — so
/// the degraded answer is a contract, not an accident: the key comes back
/// as itself, visibly not a label, and nothing throws while a menu is being
/// built.
///
/// <para>
/// Nothing is registered here on purpose. The locator is process-wide, and
/// a test class that puts a source into it would change what every other
/// class running beside it sees (see <c>Fakes/FakeTextSource</c>, which is
/// passed by hand for exactly that reason).
/// </para>
/// </summary>
public class TextFallbackTests {
    [Fact]
    public void Get_WithNoSourceRegistered_ReturnsTheKey() {
        Assert.Equal("Menu.Copy", Text.Get("Menu.Copy"));
    }


    [Fact]
    public void Format_WithNoSourceRegistered_ReturnsTheKeyRatherThanThrowing() {
        // The key has no placeholders, so string.Format would normally be
        // harmless — but the arguments are what a caller passes for the
        // real template, and the fallback must survive them.
        Assert.Equal("Status.Deleted", Text.Format("Status.Deleted", 3, "photos"));
    }


    [Fact]
    public void Format_WithABrokenTemplate_ReturnsItUnformatted() {
        // A resx entry with {1} but one argument is a bug in the string
        // table; a file manager must not die of it mid-menu.
        Assert.Equal("Status.{1}", Text.Format("Status.{1}", "one"));
    }
}
