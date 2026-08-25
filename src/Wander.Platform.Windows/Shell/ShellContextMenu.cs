using System.Runtime.InteropServices;
using System.Text;
using Wander.Core.Logging;
using Wander.Core.Shell;
using static Wander.Platform.Windows.Shell.ShellContextMenuInterop;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Reads the classic Windows shell context menu — the one Windows 11 hides
/// behind "Show more options" and where every third-party handler (7-Zip,
/// TortoiseGit, WinRAR, antivirus scanners) still registers itself.
///
/// <para>
/// Wander does not hand the resulting <c>HMENU</c> to <c>TrackPopupMenu</c>;
/// it walks it and re-renders the items as ordinary WPF rows, so the shell's
/// entries sit inside Wander's own menu instead of next to it. The cost of
/// that choice is paid here: submenus have to be primed by hand
/// (<c>WM_INITMENUPOPUP</c>), and owner-drawn rows — whose text lives in a
/// handler-private struct — cannot be read at all and are skipped.
/// </para>
/// </summary>
public sealed class ShellContextMenu : IShellContextMenu {
    private readonly ILogger _log;


    public ShellContextMenu(ILogger log) {
        _log = log;
    }


    public IShellContextMenuSession? Open(IReadOnlyList<string> paths, string folderPath) {
        try {
            return ShellContextMenuSession.Create(paths, folderPath, _log);
        } catch (Exception ex) {
            // Third-party handlers run inside our process; a broken one must
            // cost the extension group, never the menu.
            _log.Warn($"Shell context menu unavailable for '{folderPath}': {ex.Message}");

            return null;
        }
    }
}


/// <summary>
/// One live query. Owns a COM <c>IContextMenu</c>, an <c>HMENU</c> and the
/// PIDLs they were built from — all of which must outlive the visible menu,
/// because the command the user picks is invoked through the same objects.
/// </summary>
internal sealed class ShellContextMenuSession : IShellContextMenuSession {
    /// <summary>
    /// Command ids handed to the shell. Starting above zero keeps the
    /// "nothing was picked" case unambiguous; the ceiling is the documented
    /// safe range for a single handler chain.
    /// </summary>
    private const int IdFirst = 1;
    private const int IdLast = 0x7FFF;

    /// <summary>Submenu nesting we are willing to walk. Real menus use one or two.</summary>
    private const int MaxDepth = 4;

    /// <summary>
    /// Canonical verbs Wander renders itself. Dropping them is what keeps
    /// the shell group from repeating Cut / Copy / Delete / Properties two
    /// inches below Wander's own copies. Matching on the canonical verb
    /// rather than the label is deliberate: labels are localised, verbs
    /// are not.
    /// </summary>
    private static readonly HashSet<string> _duplicateVerbs = new(StringComparer.OrdinalIgnoreCase) {
        "open", "opennewwindow", "opennewprocess", "explore", "openas",
        "cut", "copy", "paste", "pastelink", "delete", "rename", "link",
        "properties", "undo",
        "copyaspath", "windows.copyaspath", "windows.modernshare", "windows.share",
        // Windows 11 "Add to Favorites" — Wander has its own bookmarks panel.
        "pintohome", "pintohomefile",
    };

    private readonly ILogger _log;
    private readonly string _folderPath;
    private readonly List<IntPtr> _pidls = new();

    private IContextMenu? _menu;
    private IContextMenu2? _menu2;
    private IntPtr _hMenu;
    private bool _disposed;


    private ShellContextMenuSession(string folderPath, ILogger log) {
        _folderPath = folderPath;
        _log = log;
    }


    public IReadOnlyList<ShellMenuEntry> Items { get; private set; } = Array.Empty<ShellMenuEntry>();


    public static ShellContextMenuSession? Create(IReadOnlyList<string> paths, string folderPath, ILogger log) {
        var session = new ShellContextMenuSession(folderPath, log);
        try {
            if (session.Initialize(paths)) {
                return session;
            }
        } catch {
            session.Dispose();
            throw;
        }

        session.Dispose();

        return null;
    }


    public bool Invoke(int commandId) {
        // The id can only come from our own enumeration, but it crosses a
        // layer boundary before coming back — bound it rather than hand the
        // shell an offset it never issued.
        if (_disposed || _menu is null || commandId < 0 || commandId > IdLast - IdFirst) {
            return false;
        }

        IntPtr directoryW = Marshal.StringToHGlobalUni(_folderPath);
        IntPtr directoryA = Marshal.StringToHGlobalAnsi(_folderPath);
        try {
            var invoke = new CMINVOKECOMMANDINFOEX {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE,
                hwnd = GetActiveWindow(),
                // Not a string: MAKEINTRESOURCE-style command offset.
                lpVerb = (IntPtr)commandId,
                lpVerbW = (IntPtr)commandId,
                lpDirectory = directoryA,
                lpDirectoryW = directoryW,
                nShow = SW_SHOWNORMAL,
            };

            int hr = _menu.InvokeCommand(ref invoke);
            if (hr < 0) {
                _log.Warn($"Shell command {commandId} failed with HRESULT 0x{hr:X8}.");

                return false;
            }

            _log.Info($"Shell command {commandId} invoked in {_folderPath}");

            return true;
        } catch (Exception ex) {
            _log.Error($"Shell command {commandId} threw.", ex);

            return false;
        } finally {
            Marshal.FreeHGlobal(directoryW);
            Marshal.FreeHGlobal(directoryA);
        }
    }


    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;

        if (_hMenu != IntPtr.Zero) {
            DestroyMenu(_hMenu);
            _hMenu = IntPtr.Zero;
        }

        // Handler DLLs stay loaded while their object lives, so unlike the
        // short-lived RCWs elsewhere in this project these are worth
        // releasing explicitly.
        ReleaseComObject(_menu);
        _menu = null;
        _menu2 = null;

        foreach (IntPtr pidl in _pidls) {
            CoTaskMemFree(pidl);
        }
        _pidls.Clear();
    }


    // --- Construction ---------------------------------------------------

    private bool Initialize(IReadOnlyList<string> paths) {
        bool background = paths.Count == 0;
        _menu = background ? BindBackgroundMenu() : BindSelectionMenu(paths);
        if (_menu is null) {
            return false;
        }
        _menu2 = _menu as IContextMenu2;

        _hMenu = CreatePopupMenu();
        if (_hMenu == IntPtr.Zero) {
            return false;
        }

        uint flags = background ? CMF_NORMAL : CMF_NORMAL | CMF_EXPLORE;
        int hr = _menu.QueryContextMenu(_hMenu, 0, IdFirst, IdLast, flags);
        if (hr < 0) {
            _log.Warn($"QueryContextMenu failed with HRESULT 0x{hr:X8} for '{_folderPath}'.");

            return false;
        }

        // The root popup needs the same wake-up call as its submenus: some
        // handlers only attach their bitmaps (and finish their labels) once
        // they see WM_INITMENUPOPUP, which a real menu would send and we
        // otherwise never do.
        PrimeSubmenu(_hMenu, 0);
        Items = ReadMenu(_hMenu, depth: 0);

        return Items.Count > 0;
    }

    private IContextMenu? BindSelectionMenu(IReadOnlyList<string> paths) {
        IShellFolder? parent = null;
        var children = new List<IntPtr>();

        foreach (string path in paths) {
            if (SHParseDisplayName(path, IntPtr.Zero, out IntPtr full, 0, out _) < 0 || full == IntPtr.Zero) {
                continue;
            }
            // The child PIDLs below point *into* these, so they stay alive
            // until Dispose rather than being freed per item.
            _pidls.Add(full);

            if (parent is null) {
                var folderIid = IID_IShellFolder;
                if (SHBindToParent(full, ref folderIid, out object folderObject, out IntPtr child) < 0) {
                    continue;
                }
                parent = folderObject as IShellFolder;
                if (parent is null) {
                    continue;
                }
                children.Add(child);
            } else {
                children.Add(ILFindLastID(full));
            }
        }

        if (parent is null || children.Count == 0) {
            return null;
        }

        var menuIid = IID_IContextMenu;
        int hr = parent.GetUIObjectOf(
            GetActiveWindow(), (uint)children.Count, children.ToArray(), ref menuIid, IntPtr.Zero, out object menu);

        return hr >= 0 ? menu as IContextMenu : null;
    }

    private IContextMenu? BindBackgroundMenu() {
        if (string.IsNullOrEmpty(_folderPath)) {
            return null;
        }
        if (SHParseDisplayName(_folderPath, IntPtr.Zero, out IntPtr pidl, 0, out _) < 0 || pidl == IntPtr.Zero) {
            return null;
        }
        _pidls.Add(pidl);

        var folderIid = IID_IShellFolder;
        if (SHBindToObject(IntPtr.Zero, pidl, IntPtr.Zero, ref folderIid, out object folderObject) < 0) {
            return null;
        }
        if (folderObject is not IShellFolder folder) {
            return null;
        }

        // The folder-background menu comes from the *view* object, not from
        // GetUIObjectOf — that one needs items to act on.
        var menuIid = IID_IContextMenu;

        return folder.CreateViewObject(GetActiveWindow(), ref menuIid, out object menu) >= 0
            ? menu as IContextMenu
            : null;
    }


    // --- Menu walking ---------------------------------------------------

    private List<ShellMenuEntry> ReadMenu(IntPtr hMenu, int depth) {
        var entries = new List<ShellMenuEntry>();
        int count = GetMenuItemCount(hMenu);

        for (uint i = 0; i < count; i++) {
            var info = new MENUITEMINFO {
                cbSize = Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_ID | MIIM_STATE | MIIM_SUBMENU | MIIM_FTYPE | MIIM_BITMAP,
            };
            if (!GetMenuItemInfo(hMenu, i, true, ref info)) {
                continue;
            }

            if ((info.fType & MFT_SEPARATOR) != 0) {
                entries.Add(new ShellMenuEntry { IsSeparator = true });
                continue;
            }

            string header = StripAccelerators(ReadItemText(hMenu, i));
            if (string.IsNullOrWhiteSpace(header)) {
                // Owner-drawn rows keep their label in dwItemData, in a
                // layout only their own handler understands.
                if ((info.fType & MFT_OWNERDRAW) != 0) {
                    _log.Warn($"Shell menu: skipping owner-drawn item at index {i}.");
                }
                continue;
            }

            bool enabled = (info.fState & MFS_GRAYED) == 0;
            byte[]? icon = ShellMenuIcons.ToPng(info.hbmpItem);

            if (info.hSubMenu != IntPtr.Zero) {
                if (depth >= MaxDepth) {
                    continue;
                }
                PrimeSubmenu(info.hSubMenu, i);
                var children = ReadMenu(info.hSubMenu, depth + 1);
                if (children.Count == 0) {
                    continue;
                }
                entries.Add(new ShellMenuEntry {
                    Header = header,
                    IsEnabled = enabled,
                    IconPng = icon,
                    // A popup has no verb of its own; the closest thing is
                    // whatever its contents identify it as.
                    Verb = depth == 0 ? DeriveSubmenuVerb(children) : string.Empty,
                    Children = children,
                });
                continue;
            }

            int id = (int)info.wID;
            if (id < IdFirst || id > IdLast) {
                continue;
            }

            int command = id - IdFirst;
            // Depth 1 too, not just the top level: a popup's placement is
            // decided by whether *anything* inside it publishes a verb, so
            // a partial answer there would misfile it. Stopping at the first
            // verb found was tried and measured — the saving was inside the
            // noise, and it made the openas lookup depend on child order.
            string? verb = depth <= 1 ? GetCanonicalVerb(command) : null;
            // Only the top level is de-duplicated: a "Copy" *inside* a
            // handler's own submenu means something else entirely.
            if (depth == 0 && verb is not null && _duplicateVerbs.Contains(verb)) {
                continue;
            }

            entries.Add(new ShellMenuEntry {
                CommandId = command,
                Header = header,
                IsEnabled = enabled,
                IconPng = icon,
                Verb = verb ?? string.Empty,
            });
        }

        return entries;
    }

    /// <summary>
    /// Gives a lazily-built submenu the message it would have received from
    /// a real popup, so its items exist by the time we enumerate them.
    /// </summary>
    private void PrimeSubmenu(IntPtr hSubMenu, uint index) {
        if (_menu2 is null) {
            return;
        }
        try {
            _menu2.HandleMenuMsg(WM_INITMENUPOPUP, hSubMenu, (IntPtr)index);
        } catch (Exception ex) {
            _log.Warn($"Shell menu: WM_INITMENUPOPUP rejected ({ex.Message}).");
        }
    }

    /// <summary>
    /// Best guess at what a nameless popup is. <c>QueryContextMenu</c> gives
    /// submenu headers no verb at all, so the only handle we have is what
    /// they contain: the shell's own "Open with" popup is the one holding a
    /// <c>openas</c> leaf. Callers use this to place the popup rather than
    /// to invoke it.
    /// </summary>
    private static string DeriveSubmenuVerb(List<ShellMenuEntry> children) {
        foreach (var child in children) {
            if (string.Equals(child.Verb, "openas", StringComparison.OrdinalIgnoreCase)) {
                return "openas";
            }
        }

        return string.Empty;
    }

    private string? GetCanonicalVerb(int command) {
        if (_menu is null) {
            return null;
        }

        const int MaxChars = 260;
        var buffer = new byte[MaxChars * 2];
        try {
            if (_menu.GetCommandString((IntPtr)command, GCS_VERBW, IntPtr.Zero, buffer, MaxChars) < 0) {
                return null;
            }
        } catch (Exception) {
            // Plenty of handlers return E_NOTIMPL here, and a few throw.
            // Either way we simply don't know the verb.
            return null;
        }

        string text = Encoding.Unicode.GetString(buffer);
        int end = text.IndexOf('\0');

        return end >= 0 ? text[..end] : text;
    }

    private static string ReadItemText(IntPtr hMenu, uint index) {
        int length = GetMenuString(hMenu, index, null, 0, MF_BYPOSITION);
        if (length <= 0) {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        GetMenuString(hMenu, index, buffer, buffer.Capacity, MF_BYPOSITION);

        return buffer.ToString();
    }

    /// <summary>
    /// Removes the Win32 <c>&amp;</c> accelerator markers. WPF marks access
    /// keys with an underscore instead, so leaving them in would render a
    /// literal ampersand in front of every second word.
    /// </summary>
    private static string StripAccelerators(string text) {
        if (text.Length == 0) {
            return text;
        }

        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++) {
            if (text[i] != '&') {
                result.Append(text[i]);
                continue;
            }
            // "&&" is an escaped literal ampersand; a lone "&" is a marker.
            if (i + 1 < text.Length && text[i + 1] == '&') {
                result.Append('&');
                i++;
            }
        }

        return result.ToString().Trim();
    }

    private static void ReleaseComObject(object? instance) {
        if (instance is null || !Marshal.IsComObject(instance)) {
            return;
        }
        try {
            Marshal.FinalReleaseComObject(instance);
        } catch (Exception) {
            // Releasing is best-effort cleanup; a handler that objects
            // must not take the app down on menu close.
        }
    }
}
