using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Wander.Core.Appearance;
using Wander.Core.Logging;

namespace Wander.App.Util;

/// <summary>
/// Light or dark, for the whole session and every window in it.
///
/// <para>
/// The light palette (<c>Resources/Palette.Light.xaml</c>) is always in the
/// application's dictionaries, by way of <c>Shared.xaml</c>; the dark one
/// (<c>Palette.Dark.xaml</c>, the same keys) is laid over it, at the end of
/// <c>Application.Resources</c>, and taken off again. Everything painted by
/// <c>DynamicResource</c> repaints - nothing is rebuilt, nothing is reread,
/// the selection and the scroll position stay where they were. What WPF
/// does not draw - a window's title bar - is told separately
/// (<see cref="Attach"/>).
/// </para>
///
/// <para>
/// Not a swap inside <c>Shared.xaml</c>: a dictionary loaded through
/// <c>Source</c> hands its list of merged dictionaries to an inner instance
/// with no owners, and a change to that list reaches no window - every
/// colour stayed where it was (stand 2026-10-09, real windows). The
/// application's own dictionary tells its windows.
/// </para>
///
/// <para>
/// Following Windows means the app mode in Personalisation - Colours,
/// read at start and again whenever Windows broadcasts a change of it.
/// </para>
/// </summary>
public static class InterfaceTheme {
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightName = "Palette.Light.xaml";
    private const string DarkName = "Palette.Dark.xaml";

    private static readonly Uri _lightSource = new($"pack://application:,,,/Wander;component/Resources/{LightName}");
    private static readonly Uri _darkSource = new($"pack://application:,,,/Wander;component/Resources/{DarkName}");

    private static UiTheme _choice = UiTheme.System;
    private static bool _watching;
    private static ResourceDictionary? _dark;


    /// <summary>Whether the windows are drawn dark now.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Whether Windows has its applications in the dark mode - what following Windows means on this machine now.</summary>
    public static bool WindowsIsDark => ReadWindowsIsDark();

    /// <summary>After the palette was swapped: for what derives colours from it rather than painting with it (the gallery, the code view, the web view).</summary>
    public static event Action? Changed;


    /// <summary>
    /// Applies the user's <paramref name="choice"/>: at start, before the
    /// first window, and again from the settings page. Following Windows
    /// starts listening for Windows' own switch; the listening stays on, a
    /// later fixed choice just makes it change nothing.
    /// </summary>
    public static void Apply(UiTheme choice) {
        _choice = choice;
        if (choice == UiTheme.System && !_watching) {
            _watching = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        Show(UiThemes.IsDark(choice, ReadWindowsIsDark()));
    }

    /// <summary>
    /// Keeps <paramref name="window"/>'s title bar in the theme: dark when
    /// the theme is, from its first frame on (set before it is shown) and
    /// after every switch while it is open. Every window with a title bar
    /// calls this in its constructor, as it calls
    /// <see cref="App.ParkIfHeadless"/>.
    /// </summary>
    public static void Attach(Window window) {
        window.SourceInitialized += (_, _) => PaintFrame(window);
    }

    /// <summary>
    /// A palette for a part of a window drawn in the other theme
    /// (<see cref="ThemeScope"/>): a fresh instance for each part, merged
    /// into that one element's resources only - a handful of parts, each
    /// loaded when its surround turns, so nothing has to rely on how WPF
    /// treats one dictionary with several owners.
    /// </summary>
    public static ResourceDictionary ScopePalette(bool dark) {
        return new ResourceDictionary { Source = dark ? _darkSource : _lightSource };
    }

    /// <summary>Whether <paramref name="palette"/> is the dark one - which of the two a scope holds.</summary>
    public static bool IsDarkPalette(ResourceDictionary palette) {
        return IsNamed(palette, DarkName);
    }


    private static void Show(bool dark) {
        IsDark = dark;
        if (Application.Current is not { } app) {
            return;
        }

        // Last in the list, so a lookup finds it before Shared.xaml's light one.
        var merged = app.Resources.MergedDictionaries;
        if ((_dark is not null && merged.Contains(_dark)) == dark) {
            return;
        }

        if (dark) {
            merged.Add(_dark ??= new ResourceDictionary { Source = _darkSource });
        } else {
            merged.Remove(_dark!);
        }
        Log.Info($"Theme: {(dark ? "dark" : "light")} (setting {_choice}, Windows {(ReadWindowsIsDark() ? "dark" : "light")})");
        foreach (Window window in app.Windows) {
            PaintFrame(window);
        }
        Changed?.Invoke();
    }

    private static bool IsNamed(ResourceDictionary dictionary, string name) {
        return dictionary.Source is { } source && source.OriginalString.EndsWith(name, StringComparison.OrdinalIgnoreCase);
    }

    private static void PaintFrame(Window window) {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) {
            return;
        }

        int dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        // What shows for a frame before WPF draws the window's own face -
        // white, a flash on a dark window - is the composition target's
        // clear colour.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target && window.Background is SolidColorBrush face) {
            target.BackgroundColor = face.Color;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) {
        if (e.Category != UserPreferenceCategory.General || _choice != UiTheme.System) {
            return;
        }

        // Raised on Windows' broadcast thread; the dictionaries belong to the UI thread.
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => {
            if (_choice == UiTheme.System) {
                Show(ReadWindowsIsDark());
            }
        });
    }

    private static bool ReadWindowsIsDark() {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        } catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) {
            return false;
        }
    }


    // --- P/Invoke ---------------------------------------------------------------------------

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE: the title bar drawn dark. 20 from Windows 10 20H1 on - the oldest Wander runs on.</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;


    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
