namespace Wander.Core.Appearance;

/// <summary>The colour scheme as the user picked it on the settings page; stored by name.</summary>
public enum UiTheme {
    /// <summary>Whatever Windows uses for applications - see <see cref="UiThemes.IsDark"/>.</summary>
    System,
    Light,
    Dark,
}


public static class UiThemes {
    /// <summary>
    /// Whether the window is drawn dark for the user's <paramref name="choice"/>
    /// on a Windows whose applications are set to dark
    /// (<paramref name="windowsAppsDark"/>: "Choose your app mode" in
    /// Personalisation - Colours). A choice of its own wins; following
    /// Windows follows that one switch, not the taskbar's.
    /// </summary>
    public static bool IsDark(UiTheme choice, bool windowsAppsDark) {
        return choice switch {
            UiTheme.Light => false,
            UiTheme.Dark => true,
            _ => windowsAppsDark,
        };
    }
}
