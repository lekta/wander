using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wander.Core.Menu;
using Wander.Core.Shell;

namespace Wander.App.Menu;

/// <summary>What a <see cref="MenuCommandId"/> actually runs, plus its argument if it takes one.</summary>
public sealed record MenuBinding(ICommand Command, object? Parameter = null);


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


    public ContextMenu Build(IReadOnlyList<MenuEntry> model, IShellContextMenuSession? session) {
        var menu = new ContextMenu();
        var pending = new PendingShellCommand();

        Fill(menu.Items, model, pending);

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


    private void Fill(ItemCollection items, IReadOnlyList<MenuEntry> model, PendingShellCommand pending) {
        foreach (var entry in model) {
            if (entry.IsSeparator) {
                items.Add(new Separator());
                continue;
            }
            items.Add(CreateItem(entry, pending));
        }
    }

    private MenuItem CreateItem(MenuEntry entry, PendingShellCommand pending) {
        var item = new MenuItem {
            Header = EscapeHeader(entry.Header),
            InputGestureText = entry.Gesture ?? string.Empty,
        };

        if (entry.IconPng is { } png && ToImageSource(png) is { } icon) {
            item.Icon = new Image { Source = icon, Width = 16, Height = 16 };
        } else if (_glyphs.TryGetValue(entry.Id, out string? glyph)) {
            item.Icon = Glyph(glyph);
        }
        if (entry.IsDefault) {
            item.FontWeight = FontWeights.SemiBold;
        }

        if (entry.HasChildren) {
            Fill(item.Items, entry.Children, pending);

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

        if (entry.IsShellCommand) {
            int command = entry.ShellCommand;
            item.Click += (_, _) => pending.Id = command;

            return item;
        }

        if (_bindings.TryGetValue(entry.Id, out var binding)) {
            item.Command = binding.Command;
            item.CommandParameter = binding.Parameter;
        } else {
            // An id with no binding is a wiring bug, not a user-facing
            // state; showing it greyed is the least confusing failure.
            item.IsEnabled = false;
        }

        return item;
    }


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
