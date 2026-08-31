using System.Windows;
using System.Windows.Controls;
using Wander.App.Resources;
using Wander.Core.Shell;

namespace Wander.App.Views;

/// <summary>
/// Picks what to add to the context-menu table: an application, with every
/// scope it registers on, or individual file types.
///
/// <para>
/// The application list needs a full scan — all eight hundred extensions —
/// because that is the only way to know that Photoshop also hangs itself off
/// <c>.psb</c>. It costs about 150 ms and happens once, when this window
/// opens, not while the settings dialog is being browsed.
/// </para>
/// </summary>
public partial class ShellScopePicker : Window {
    private readonly List<TypeRow> _allTypes = new();


    public ShellScopePicker(IShellHandlerRegistry registry, IReadOnlyList<string> recent) {
        InitializeComponent();

        var extensions = registry.ListExtensions();
        var handlers = registry.Scan(ShellScopes.Base.Concat(extensions).ToArray());

        FillApplications(handlers);
        FillTypes(extensions, recent);
    }


    /// <summary>Scopes the user chose. Empty when the dialog was cancelled.</summary>
    public IReadOnlyList<string> SelectedScopes { get; private set; } = Array.Empty<string>();


    private void FillApplications(IReadOnlyList<ShellHandler> handlers) {
        // One entry per application, carrying the union of the scopes all of
        // its handlers sit on — which is exactly "все его расширения".
        var byApp = new Dictionary<string, HashSet<string>>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var handler in handlers) {
            if (handler.AppName.Length == 0 || handler.IsSystem || ShellVerbs.IsSuppressed(handler.Key)) {
                continue;
            }
            if (!byApp.TryGetValue(handler.AppName, out var scopes)) {
                scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byApp[handler.AppName] = scopes;
            }
            foreach (string scope in handler.Scopes) {
                scopes.Add(scope);
            }
        }

        AppList.ItemsSource = byApp
            .OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new AppRow(pair.Key, pair.Value.ToArray()))
            .ToList();
    }

    private void FillTypes(IReadOnlyList<string> extensions, IReadOnlyList<string> recent) {
        var recentSet = new HashSet<string>(recent, StringComparer.OrdinalIgnoreCase);

        // Recently right-clicked types first, then everything else. The note
        // is what tells the user why the order is not alphabetical.
        foreach (string extension in recent) {
            _allTypes.Add(new TypeRow(extension, Strings.PickerRecentNote));
        }
        foreach (string extension in extensions) {
            if (!recentSet.Contains(extension)) {
                _allTypes.Add(new TypeRow(extension, string.Empty));
            }
        }

        TypeList.ItemsSource = _allTypes;
    }


    private void TypeFilter_TextChanged(object sender, TextChangedEventArgs e) {
        string query = TypeFilter.Text.Trim();
        TypeList.ItemsSource = query.Length == 0
            ? _allTypes
            : _allTypes.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) {
        var scopes = new List<string>();
        foreach (AppRow app in AppList.SelectedItems) {
            scopes.AddRange(app.Scopes);
        }
        foreach (TypeRow type in TypeList.SelectedItems) {
            scopes.Add(type.Name);
        }

        SelectedScopes = scopes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        DialogResult = SelectedScopes.Count > 0;
        Close();
    }


    private sealed record AppRow(string Title, IReadOnlyList<string> Scopes);

    private sealed record TypeRow(string Name, string Note);
}
