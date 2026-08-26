using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wander.App.Converters;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.Icons;

namespace Wander.App.Controls;

/// <summary>
/// An <see cref="Image"/> that pulls its shell icon / thumbnail off the UI
/// thread. Used by the three file-list views, where a folder of heavy files
/// (RAW photos, video) would otherwise freeze the window: the 256 px
/// "Large" icon is a real thumbnail, and extracting one out of a 30 MB
/// .CR3 takes hundreds of milliseconds — times every visible tile.
///
/// <para>
/// Already-cached icons are set synchronously (see
/// <see cref="IIconProvider.TryGetCachedIcon"/>) so scrolling back over
/// seen items doesn't blink. Everything else goes through a worker thread;
/// <see cref="_gate"/> keeps a fast scroll from dumping hundreds of shell
/// calls onto the thread pool at once, and the generation counter drops
/// results that arrive after virtualization recycled the container onto a
/// different file.
/// </para>
/// </summary>
public sealed class AsyncIcon : Image {
    public static readonly DependencyProperty IconPathProperty =
        DependencyProperty.Register(
            nameof(IconPath), typeof(string), typeof(AsyncIcon),
            new PropertyMetadata(null, OnIconRequestChanged));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize), typeof(IconSize), typeof(AsyncIcon),
            new PropertyMetadata(IconSize.Normal, OnIconRequestChanged));

    // Shell thumbnail extraction is disk- and CPU-heavy; two at a time keeps
    // the pool free for the rest of the app while still overlapping I/O.
    private static readonly SemaphoreSlim _gate = new(2);


    private int _generation;


    public string? IconPath {
        get => (string?)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public IconSize IconSize {
        get => (IconSize)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }


    private static void OnIconRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        ((AsyncIcon)d).Reload();
    }

    private async void Reload() {
        // Bumping the generation invalidates whatever load is in flight for
        // the previous path — its result will be dropped below.
        int generation = ++_generation;

        if (IconPath is not { Length: > 0 } path) {
            Source = null;
            return;
        }

        IconSize size = IconSize;
        if (!ServiceLocator.IsRegistered<IIconProvider>()) {
            Source = null;
            return;
        }

        byte[]? cached = ServiceLocator.Get<IIconProvider>().TryGetCachedIcon(path, size);
        if (cached is not null) {
            // The only thumbnail work left on the UI thread, and only for a
            // file this session has not drawn before — see IconImageCache.
            using (PerfLog.Measure("icon.decode-ui")) {
                Source = IconImageCache.Get(path, size, cached);
            }

            return;
        }

        Source = null;
        var image = await LoadAsync(path, size, () => generation == _generation);
        if (generation == _generation) {
            Source = image;
        }
    }

    private static async Task<BitmapImage?> LoadAsync(string path, IconSize size, Func<bool> stillWanted) {
        await _gate.WaitAsync();
        try {
            // Re-check after queueing: a fast scroll through a big folder can
            // park hundreds of loads on this gate, and by the time one gets
            // through, its container has usually been recycled onto another
            // file. Doing the shell call anyway would be pure waste — and
            // shell calls are the expensive part.
            if (!stillWanted()) {
                return null;
            }

            return await Task.Run(() => {
                using (PerfLog.Measure("bg.icon-load")) {
                    byte[]? bytes = ServiceLocator.Get<IIconProvider>().GetIcon(path, size);

                    // Decoded here rather than in IconConverter so the result
                    // lands in the decoded cache: the same tile scrolled back
                    // to then costs nothing at all, instead of a decode on the
                    // UI thread the first time it comes round again.
                    return bytes is null ? null : IconImageCache.Get(path, size, bytes);
                }
            });
        } catch {
            // A missing icon is a cosmetic loss; never let it reach the
            // dispatcher's unhandled-exception handler.
            return null;
        } finally {
            _gate.Release();
        }
    }
}
