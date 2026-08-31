using Wander.Core.Persistence;
using Wander.Core.Shell;

namespace Wander.Core.Tests;

/// <summary>
/// The settings table is assembled from two sources that disagree about
/// what they know, and the rules for reconciling them are the whole of the
/// feature. Everything here is pure: no registry is touched, the handler
/// records stand in for whatever the platform layer found.
/// </summary>
public class ShellExtensionCatalogTests {

    [Fact]
    public void HandlerAndSighting_MergeIntoOneRow() {
        // The registry knows the application and the file types; the menu
        // knows the row was actually drawn. One row, both halves.
        var rows = Build(
            handlers: new[] { Handler("7-Zip", app: "7-Zip", scopes: new[] { ShellScopes.AllFiles }) },
            seen: new[] { "7-Zip" });

        var row = Assert.Single(rows);
        Assert.Equal("7-Zip", row.AppName);
        Assert.Equal(new[] { ShellScopes.AllFiles }, row.Scopes);
        Assert.True(row.IsSeen);
    }

    [Fact]
    public void OneHandlerOnManyScopes_IsOneRowWithAllOfThem() {
        // 7-Zip registers itself three times over; that is one switch, not
        // three, and the "Типы" column is what the three become.
        var rows = Build(handlers: new[] {
            Handler("7-Zip", app: "7-Zip", scopes: new[] { ShellScopes.AllFiles }),
            Handler("7-Zip", app: "", scopes: new[] { ShellScopes.Directory }),
            Handler("7-Zip", app: "7-Zip", scopes: new[] { ShellScopes.Folder }),
        });

        var row = Assert.Single(rows);
        Assert.Equal(
            new[] { ShellScopes.AllFiles, ShellScopes.Directory, ShellScopes.Folder },
            row.Scopes);
        // An empty AppName on one of the three must not erase the other two.
        Assert.Equal("7-Zip", row.AppName);
    }

    [Fact]
    public void SeenButUnknownToTheRegistry_StillGetsARow() {
        // TortoiseGit's COM handler draws "Git Commit -> master..." at popup
        // time; nothing in the registry predicts that string. Without a row
        // for it the user could never switch it off.
        var rows = Build(handlers: Array.Empty<ShellHandler>(), seen: new[] { "Git Commit..." });

        var row = Assert.Single(rows);
        Assert.Equal("Git Commit...", row.Title);
        Assert.True(row.IsSeen);
        Assert.Empty(row.Scopes);
    }

    [Fact]
    public void BlockedButUnknownToBothSources_KeepsItsWayBack() {
        // Uninstall the application and the handler leaves the registry, but
        // the block stays in state.json. Dropping the row would strand it.
        var rows = Build(
            handlers: Array.Empty<ShellHandler>(),
            blocked: new[] { "SomethingLongGone" });

        var row = Assert.Single(rows);
        Assert.True(row.IsBlocked);
    }

    [Fact]
    public void SystemHandlers_AreHiddenUnlessAskedFor() {
        var handlers = new[] {
            Handler("SendTo", app: "Windows", system: true),
            Handler("7-Zip", app: "7-Zip"),
        };

        Assert.Equal(new[] { "7-Zip" }, Build(handlers).Select(r => r.Title));
        Assert.Equal(
            new[] { "7-Zip", "SendTo" },
            Build(handlers, includeSystem: true).Select(r => r.Title).OrderBy(t => t));
    }

    [Fact]
    public void SystemHandler_ShowsAnywayOnceItIsBlockedOrSeen() {
        // Otherwise switching one off makes its own switch disappear.
        var handlers = new[] { Handler("SendTo", app: "Windows", system: true) };

        Assert.Single(Build(handlers, blocked: new[] { "SendTo" }));
        Assert.Single(Build(handlers, seen: new[] { "SendTo" }));
    }

    [Fact]
    public void VerbsWanderRendersItself_AreNotOffered() {
        // A switch for "copy" would do nothing: the row never reaches the
        // menu in the first place.
        var rows = Build(handlers: new[] {
            Handler("copy", app: "Windows"),
            Handler("pintohome", app: "Windows"),
            Handler("7-Zip", app: "7-Zip"),
        });

        Assert.Equal(new[] { "7-Zip" }, rows.Select(r => r.Title));
    }

    [Fact]
    public void SeenRowsSortFirst() {
        var rows = Build(
            handlers: new[] {
                Handler("AAA Unseen", app: "AAA"),
                Handler("ZZZ Seen", app: "ZZZ"),
            },
            seen: new[] { "ZZZ Seen" });

        Assert.Equal(new[] { "ZZZ Seen", "AAA Unseen" }, rows.Select(r => r.Title));
    }

    [Fact]
    public void BlockedState_IsReadOffTheKeyNotTheLabel() {
        var rows = Build(
            handlers: new[] { Handler("git_shell", title: "Open Git Bash here", app: "Git") },
            blocked: new[] { "git_shell" });

        var row = Assert.Single(rows);
        Assert.True(row.IsBlocked);
        Assert.Equal("Open Git Bash here", row.Title);
    }


    [Fact]
    public void TheSameRowUnderTwoKeyStyles_IsOneLine() {
        // "Git Clone" was written by a build that keyed on labels;
        // "Git Clone..." by one that keys on verbs. Two lines for one menu
        // item, each with its own checkbox, is worse than useless — tick the
        // wrong one and nothing happens.
        var rows = Build(
            handlers: Array.Empty<ShellHandler>(),
            seen: new[] { "Git Clone", "Git Clone..." });

        var row = Assert.Single(rows);
        // The verb-shaped key wins: that is what the blocklist should hold.
        Assert.Equal("Git Clone...", row.Key);
    }

    [Fact]
    public void NamelessClsidHandlers_AreNotOffered() {
        // A row reading "{9F156763-…}" next to an empty checkbox is not a
        // setting. If nothing can be said about it, it is not listed.
        var rows = Build(handlers: new[] {
            Handler("{9F156763-7844-4DC4-B2B1-901F640F5155}", title: "{9F156763-7844-4DC4-B2B1-901F640F5155}"),
            Handler("7-Zip", app: "7-Zip"),
        });

        Assert.Equal(new[] { "7-Zip" }, rows.Select(r => r.Title));
    }

    [Fact]
    public void ANamelessClsidMetInAMenu_IsNotOfferedEither() {
        // It gets into the seen list when a handler publishes its CLSID as
        // the verb. The rule is the same on both sides: if nothing can be
        // said about the row, it is not a setting.
        var rows = ShellExtensionCatalog.Build(
            Array.Empty<ShellHandler>(),
            new[] {
                new KnownShellEntry { Key = "{9F156763-7844-4DC4-B2B1-901F640F5155}", Title = "{9F156763-7844-4DC4-B2B1-901F640F5155}" },
                new KnownShellEntry { Key = "7-Zip", Title = "7-Zip" },
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new[] { "7-Zip" }, rows.Select(r => r.Title));
    }

    [Fact]
    public void ANamelessClsidThatSaysWhatItDoes_IsKept() {
        // Help text is the whole test: with it the row can be described, so
        // there is something to decide about.
        var rows = ShellExtensionCatalog.Build(
            Array.Empty<ShellHandler>(),
            new[] {
                new KnownShellEntry {
                    Key = "{9F156763-7844-4DC4-B2B1-901F640F5155}",
                    Title = "{9F156763-7844-4DC4-B2B1-901F640F5155}",
                    Help = "Открывает панель управления",
                },
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Single(rows);
    }

    [Fact]
    public void NamelessClsid_StillAppearsOnceItIsBlocked() {
        var rows = Build(
            handlers: new[] { Handler("{9F156763-7844-4DC4-B2B1-901F640F5155}", title: "{9F156763-7844-4DC4-B2B1-901F640F5155}") },
            blocked: new[] { "{9F156763-7844-4DC4-B2B1-901F640F5155}" });

        Assert.True(Assert.Single(rows).IsBlocked);
    }

    [Fact]
    public void RegistryLabelBeatsTheKeyAsATitle() {
        var rows = ShellExtensionCatalog.Build(
            new[] { Handler("git_shell", title: "Open Git Bash here", app: "Git") },
            new[] { new KnownShellEntry { Key = "git_shell", Title = "git_shell" } },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal("Open Git Bash here", Assert.Single(rows).Title);
    }

    [Fact]
    public void DescriptionComesFromTheMenu_NotTheRegistry() {
        // Only IContextMenu ever says what a row does; the registry has no
        // field for it.
        var rows = ShellExtensionCatalog.Build(
            new[] { Handler("7-Zip", app: "7-Zip") },
            new[] { new KnownShellEntry { Key = "7-Zip", Title = "7-Zip", Help = "Работа с архивами" } },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal("Работа с архивами", Assert.Single(rows).Help);
    }


    private static IReadOnlyList<ShellExtensionRow> Build(
        IReadOnlyList<ShellHandler> handlers,
        IReadOnlyList<string>? seen = null,
        IReadOnlyList<string>? blocked = null,
        bool includeSystem = false) {

        return ShellExtensionCatalog.Build(
            handlers,
            (seen ?? Array.Empty<string>()).Select(k => new KnownShellEntry { Key = k, Title = k }).ToArray(),
            new HashSet<string>(blocked ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
            includeSystem);
    }

    private static ShellHandler Handler(
        string key,
        string? title = null,
        string app = "",
        string[]? scopes = null,
        bool system = false) {


        return new ShellHandler {
            Key = key,
            Title = title ?? key,
            AppName = app,
            Scopes = scopes ?? Array.Empty<string>(),
            IsSystem = system,
        };
    }
}
