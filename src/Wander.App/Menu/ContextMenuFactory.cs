using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wander.Core;
using Wander.Core.Icons;
using Wander.Core.Menu;
using Wander.Core.Shell;
using Wander.Core.Workspace;

namespace Wander.App.Menu;

/// <summary>What a <see cref="MenuCommandId"/> actually runs, plus its argument if it takes one.</summary>
public sealed record MenuBinding(ICommand Command, object? Parameter = null);


/// <summary>
/// The parameter an item of a context menu runs its command with: the
/// menu's snapshot, taken as it opened, and the item's own argument (a
/// catalog action's id). A command that gets one acts on the snapshot's
/// subject; with none - from a key - on the target as it is at that moment.
/// </summary>
public sealed record MenuCall(MenuContext Context, object? Argument);


/// <summary>
/// Renders a <see cref="MenuEntry"/> list into a live WPF
/// <see cref="ContextMenu"/>. This is the whole UI half of the context-menu
/// feature: shape and enablement were decided in Core, so what is left here
/// is widgets, icons, and the one piece of real behaviour — when a
/// third-party command may be invoked.
///
/// <para>
/// Shell commands are deliberately <b>not</b> run from the item's Click
/// handler. Handlers routinely open modal dialogs of their own ("Add to
/// archive…", "Commit…"), and starting one while the popup is still
/// unwinding leaves the menu painted on top of it. So a click records the
/// pick, and the invocation happens once the menu has actually closed.
/// </para>
///
/// <para>
/// The session itself belongs to <see cref="ShellMenuCache"/>, not to the
/// menu — it usually outlives one right-click so the next one is instant.
/// Nothing here disposes it.
/// </para>
/// </summary>
public sealed class ContextMenuFactory {
    /// <summary>
    /// Built-in rows that carry an icon. Both sit among rows that have one —
    /// the terminal among the third-party entries, "Папка" among the shell's
    /// own file templates — and the gap next to them read as a picture that
    /// failed to load rather than as a plain item.
    ///
    /// <para>
    /// Segoe MDL2 Assets is the shell's own icon font: nothing to ship, and
    /// it is drawn at whatever DPI the rest of the menu is. A bitmap from
    /// the terminal's own exe would have been the other option, but it means
    /// a shell call on every menu build for a picture that never changes —
    /// and nothing to draw at all where Windows Terminal is not installed.
    /// </para>
    /// </summary>
    private static readonly Dictionary<MenuCommandId, string> _glyphs = new() {
        [MenuCommandId.OpenInTerminal] = "\uE756",
        // F12B, not the "Folder" / "NewFolder" codepoints: those two are MDL2
        // folder shapes drawn edge-on, and at 14 px they read as a folder
        // tipped on its side. F12B is the plain landscape one.
        [MenuCommandId.NewFolder] = "\uF12B",
    };


    private readonly IReadOnlyDictionary<MenuCommandId, MenuBinding> _bindings;
    private readonly Action _afterShellCommand;


    /// <param name="bindings">Built-in id → command map, assembled by the window.</param>
    /// <param name="afterShellCommand">
    /// Run after a third-party command succeeds — it may have created,
    /// renamed or deleted files behind our back, so both the listing and
    /// the cached shell answer are stale.
    /// </param>
    public ContextMenuFactory(
        IReadOnlyDictionary<MenuCommandId, MenuBinding> bindings,
        Action afterShellCommand) {
        _bindings = bindings;
        _afterShellCommand = afterShellCommand;
    }


    /// <param name="context">
    /// The menu's snapshot, handed to every item's command (<see cref="MenuCall"/>);
    /// null for a menu whose bindings carry everything themselves (the drop menu).
    /// </param>
    public ContextMenu Build(IReadOnlyList<MenuEntry> model, IShellContextMenuSession? session, MenuContext? context = null) {
        var menu = new ContextMenu();
        var pending = new PendingShellCommand();

        Fill(menu.Items, model, pending, context);

        menu.Closed += (sender, _) => {
            // Submenus raise SubmenuClosed, not Closed, but a stray bubble
            // would dispose the session mid-menu — hence the identity check.
            if (!ReferenceEquals(sender, menu)) {
                return;
            }
            menu.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                if (pending.Id >= 0 && session is not null && session.Invoke(pending.Id)) {
                    _afterShellCommand();
                }
            }));
        };

        return menu;
    }

    /// <summary>
    /// Replaces the rows of a menu that is not a context menu - the header's
    /// Operations menu. The shell is never asked there, so there is no
    /// session and nothing to invoke once the menu closes.
    /// </summary>
    public void Populate(ItemCollection items, IReadOnlyList<MenuEntry> model, MenuContext? context = null) {
        items.Clear();
        Fill(items, model, pending: null, context);
    }


    private void Fill(ItemCollection items, IReadOnlyList<MenuEntry> model, PendingShellCommand? pending, MenuContext? context) {
        foreach (var entry in model) {
            if (entry.IsSeparator) {
                items.Add(new Separator());
                continue;
            }
            items.Add(CreateItem(entry, pending, context));
        }
    }

    private MenuItem CreateItem(MenuEntry entry, PendingShellCommand? pending, MenuContext? context) {
        var item = new MenuItem {
            Header = EscapeHeader(entry.Header),
            InputGestureText = entry.Gesture ?? string.Empty,
        };

        if (entry.IconPng is { } png && ToImageSource(png) is { } icon) {
            item.Icon = new Image { Source = icon, Width = 16, Height = 16 };
        } else if (entry.IconPath is { } path && FileIcon(path) is { } fileIcon) {
            // The program a custom action runs, drawn the way the shell draws it.
            item.Icon = new Image { Source = fileIcon, Width = 16, Height = 16 };
        } else if (_glyphs.TryGetValue(entry.Id, out string? glyph)) {
            item.Icon = Glyph(glyph);
        }
        if (entry.IsDefault) {
            item.FontWeight = FontWeights.SemiBold;
        }
        // The reason a header row is greyed. Shown on the disabled row,
        // which WPF does not do unless told: that is the whole point.
        if (entry.Tooltip is { } tooltip) {
            item.ToolTip = tooltip;
            ToolTipService.SetShowOnDisabled(item, true);
        }

        if (entry.HasChildren) {
            Fill(item.Items, entry.Children, pending, context);

            return item;
        }

        if (entry.IsCheckable) {
            item.IsCheckable = true;
            item.IsChecked = entry.IsChecked;
        }

        // A row the builder disabled gets no command at all: binding one
        // would hand enablement back to CanExecute, which knows less about
        // the context than the builder does.
        if (!entry.IsEnabled) {
            item.IsEnabled = false;

            return item;
        }

        // Without a pending box there is no session to run a shell row in;
        // such a row falls through to the unbound-id case below.
        if (entry.IsShellCommand && pending is not null) {
            int command = entry.ShellCommand;
            item.Click += (_, _) => pending.Id = command;

            return item;
        }

        if (_bindings.TryGetValue(entry.Id, out var binding)) {
            item.Command = binding.Command;
            // A row that names its own argument (a catalog action's id)
            // wins over the binding's fixed one; the menu's snapshot rides
            // along with it, so the item acts on what the menu was opened
            // on, whenever WPF gets round to running it.
            object? argument = entry.Argument ?? binding.Parameter;
            item.CommandParameter = context is null ? argument : new MenuCall(context, argument);
        } else {
            // An id with no binding is a wiring bug, not a user-facing
            // state; showing it greyed is the least confusing failure.
            item.IsEnabled = false;
        }

        return item;
    }


    private static TextBlock Glyph(string text) {
        return new TextBlock {
            Text = text,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }


    /// <summary>
    /// WPF reads a lone underscore in a header as an access-key marker and
    /// swallows it. Shell entries quote real file names ("Add to
    /// my_archive.7z"), so every underscore has to be doubled.
    /// </summary>
    private static string EscapeHeader(string header) {
        return header.Replace("_", "__");
    }


    private static ImageSource? FileIcon(string path) {
        return ServiceLocator.Get<IIconProvider>().GetIcon(path, IconSize.Small) is { } png
            ? ToImageSource(png)
            : null;
    }

    private static ImageSource? ToImageSource(byte[] png) {
        try {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();

            return image;
        } catch (Exception) {
            return null;
        }
    }


    /// <summary>
    /// Mutable box for the id the user clicked, shared between the click
    /// handlers and the Closed handler of one menu instance.
    /// </summary>
    private sealed class PendingShellCommand {
        public int Id { get; set; } = -1;
    }
}
