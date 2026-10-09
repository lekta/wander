using System.Windows;

namespace Wander.App.Util;

/// <summary>
/// A part of a window drawn in the other theme: the gallery on a dark
/// surround while the window is light, the bar of the full screen, which is
/// dark whatever the window is. Set <c>ThemeScope.Dark</c> on the element
/// to the lightness of what it is drawn on, and everything inside that
/// paints from the palette by <c>DynamicResource</c> - a scroll bar, a
/// star, a button's face, a caption - takes that theme's colours: the
/// element's own resources hold the other palette, and a resource lookup
/// finds them before the application's.
///
/// <para>
/// Nothing is added while the element's lightness matches the window's
/// theme; a switch of the theme re-decides. Popups - tooltips, menus - are
/// separate windows and keep the window's theme.
/// </para>
/// </summary>
public static class ThemeScope {
    /// <summary>True: the element sits on a dark surface; false: on a light one; unset: on the window's own.</summary>
    public static readonly DependencyProperty DarkProperty = DependencyProperty.RegisterAttached(
        "Dark", typeof(bool?), typeof(ThemeScope), new PropertyMetadata(null, OnDarkChanged));

    /// <summary>The palette the element merged in, to take out again.</summary>
    private static readonly DependencyProperty _appliedProperty = DependencyProperty.RegisterAttached(
        "Applied", typeof(ResourceDictionary), typeof(ThemeScope), new PropertyMetadata(null));

    /// <summary>The handler that follows the theme's switches for the element, once it has one.</summary>
    private static readonly DependencyProperty _watchingProperty = DependencyProperty.RegisterAttached(
        "Watching", typeof(Action), typeof(ThemeScope), new PropertyMetadata(null));


    public static bool? GetDark(DependencyObject element) {
        return (bool?)element.GetValue(DarkProperty);
    }

    public static void SetDark(DependencyObject element, bool? value) {
        element.SetValue(DarkProperty, value);
    }


    private static void OnDarkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is not FrameworkElement element) {
            return;
        }

        Watch(element);
        Update(element);
    }

    /// <summary>Re-decides on every switch of the theme while the element is loaded.</summary>
    private static void Watch(FrameworkElement element) {
        if (element.GetValue(_watchingProperty) is not null) {
            return;
        }

        Action onChanged = () => Update(element);
        element.SetValue(_watchingProperty, onChanged);
        if (element.IsLoaded) {
            InterfaceTheme.Changed += onChanged;
        }
        element.Loaded += (_, _) => {
            InterfaceTheme.Changed -= onChanged;
            InterfaceTheme.Changed += onChanged;
            Update(element);
        };
        element.Unloaded += (_, _) => InterfaceTheme.Changed -= onChanged;
    }

    private static void Update(FrameworkElement element) {
        bool? wanted = GetDark(element) is bool dark && dark != InterfaceTheme.IsDark ? dark : null;
        var applied = (ResourceDictionary?)element.GetValue(_appliedProperty);
        bool? has = applied is null ? null : InterfaceTheme.IsDarkPalette(applied);
        if (has == wanted) {
            return;
        }

        var merged = element.Resources.MergedDictionaries;
        if (applied is not null) {
            merged.Remove(applied);
        }
        var palette = wanted is bool d ? InterfaceTheme.ScopePalette(d) : null;
        if (palette is not null) {
            merged.Add(palette);
        }
        element.SetValue(_appliedProperty, palette);
    }
}
