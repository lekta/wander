using Wander.Core.Localization;

namespace Wander.Core.Shell;

/// <summary>
/// The registry keys a context-menu handler can hang itself off, and what
/// to call them in the settings table.
///
/// <para>
/// Two kinds, and the difference matters to the user: the <b>base</b>
/// scopes below apply to everything of a kind — <c>*</c> is every file
/// there is, which is where 7-Zip and the antivirus live — while an
/// extension scope (<c>.7z</c>) applies to one file type. That is why the
/// settings table ships with the base scopes already in it and asks before
/// adding extensions: the base ones are the handful that show up in every
/// menu, the extensions are the long tail.
/// </para>
/// </summary>
public static class ShellScopes {
    /// <summary>Every file. Not folders — that surprises people.</summary>
    public const string AllFiles = "*";

    /// <summary>Every file and folder alike.</summary>
    public const string AllFilesystemObjects = "AllFilesystemObjects";

    /// <summary>A folder, right-clicked as an item in the listing.</summary>
    public const string Directory = "Directory";

    /// <summary>The empty space of the folder currently open.</summary>
    public const string DirectoryBackground = @"Directory\Background";

    /// <summary>Folders including the shell's virtual ones (zip archives, namespaces).</summary>
    public const string Folder = "Folder";

    /// <summary>A drive root.</summary>
    public const string Drive = "Drive";


    /// <summary>
    /// What the settings table starts out showing. Everything here appears
    /// in almost every menu the user opens, so pre-filling it costs one
    /// short scan and saves the "why is this table empty" question.
    /// </summary>
    public static IReadOnlyList<string> Base { get; } = new[] {
        AllFiles,
        AllFilesystemObjects,
        Directory,
        DirectoryBackground,
        Folder,
        Drive,
    };


    private static readonly Dictionary<string, string> _titleKeys =
        new(StringComparer.OrdinalIgnoreCase) {
            [AllFiles] = "ScopeAllFiles",
            [AllFilesystemObjects] = "ScopeAllObjects",
            [Directory] = "ScopeDirectory",
            [DirectoryBackground] = "ScopeBackground",
            [Folder] = "ScopeFolder",
            [Drive] = "ScopeDrive",
        };


    public static bool IsBase(string scope) {
        return _titleKeys.ContainsKey(scope);
    }

    /// <summary>
    /// Human-readable name of a scope. Extensions are shown as themselves —
    /// ".7z" needs no translating and inventing "Архив 7-Zip" for it would
    /// be a lie about which key we actually matched.
    /// </summary>
    public static string Title(string scope) {
        return _titleKeys.TryGetValue(scope, out string? key) ? Text.Get(key) : scope;
    }

    /// <summary>
    /// The extension scope a path belongs to, lowercased with its dot —
    /// or null for a folder and for a name with no extension, neither of
    /// which has one. Used to remember where menus were opened, so the
    /// "Добавить" picker can lead with the types actually being browsed.
    /// </summary>
    public static string? ExtensionOf(string? path) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        int dot = path.LastIndexOf('.');
        if (dot <= 0 || dot == path.Length - 1) {
            return null;
        }
        // A dot in a directory name is not an extension of the file that
        // has none: "C:\v1.2\README" must not come back as ".2\README".
        if (path.IndexOfAny(new[] { '\\', '/' }, dot) >= 0) {
            return null;
        }

        return path[dot..].ToLowerInvariant();
    }
}
