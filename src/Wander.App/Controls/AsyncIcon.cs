using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    /// <summary>One second between the two attempts at a failed icon.</summary>
    private const int RetryDelayMs = 1000;

    public static readonly DependencyProperty IconPathProperty =
        DependencyProperty.Register(
            nameof(IconPath), typeof(string), typeof(AsyncIcon),
            new PropertyMetadata(null, OnIconRequestChanged));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize), typeof(IconSize), typeof(AsyncIcon),
            new PropertyMetadata(IconSize.Normal, OnIconRequestChanged));


    // Thumbnail extraction is disk- and CPU-heavy, so it is metered rather
    // than let loose on the thread pool. Four rather than two: two was
    // sized against the shell call, which does not parallelise, and it
    // became the ceiling once RAW files stopped going through it — a
    // screenful of photographs measured 75 ms per file through the shell
    // and 3 ms per file decoded here (see RawThumbnail), at which point the
    // gate, not the work, was what the user was waiting for. Four still
    // leaves the pool to the rest of the app.
    private static readonly SemaphoreSlim _gate = new(4);



    private int _generation;
    private bool _detached;


    public AsyncIcon() {
        // Leaving the tree ends the request. A container discarded by a
        // listing swap is never recycled and never has its path changed,
        // so nothing else would bump the generation: its load kept its
        // place at the gate and its delivery its place in the queue, and
        // a folder walked through quickly left hundreds of both behind -
        // exactly what the next folder's thumbnails then waited on.
        Unloaded += (_, _) => {
            _generation++;
            _detached = true;
        };
        // A recycled container that comes back for the same file gets no
        // property change to reload it, so it asks again here.
        Loaded += (_, _) => {
            if (_detached) {
                Reload();
            }
        };
    }


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

    private void Reload() {
        // Bumping the generation invalidates whatever load is in flight for
        // the previous path — its result will be dropped below.
        int generation = ++_generation;
        _detached = false;

        if (IconPath is not { Length: > 0 } path) {
            Source = null;
            return;
        }

        IconSize size = IconSize;
        var icons = ServiceLocator.Get<IIconProvider>();

        byte[]? cached = icons.TryGetCachedIcon(path, size);
        if (cached is not null) {
            // Drawn before this session: set synchronously, so scrolling
            // back over seen tiles doesn't blink.
            if (IconImageCache.TryGetDecoded(path, size, out var decoded)) {
                Source = decoded;

                return;
            }

            // A 16-px glyph decodes in microseconds and is what the tree
            // and the Details column run on — it stays synchronous. The
            // thumbnail sizes are the expensive decodes (up to 41 ms each,
            // hundreds per second on a folder revisit — icon.decode-ui in
            // the session log), and only they take the async road.
            if (IsLightweight(size)) {
                using (PerfLog.Measure("icon.decode-ui")) {
                    Source = IconImageCache.Get(path, size, cached);
                }

                return;
            }

            Source = null;
            _ = DecodeAndApplyAsync(path, size, cached, generation);

            return;
        }

        Source = null;
        _ = LoadAndApplyAsync(path, size, generation);
    }


    /// <summary>
    /// Small and Normal are one glyph per file *type* — cheap to decode,
    /// few in number, and what the folder panels and the Details view live
    /// on. They are delivered at Normal priority, ahead of the thumbnail
    /// stream: a tree whose rows have no icons reads as broken, while a
    /// tile whose picture arrives a beat later reads as loading. Medium
    /// and Large are per-file thumbnails — the heavy stream that must
    /// never starve input, nor the listing they decorate, so they land at
    /// ContextIdle.
    /// </summary>
    private static bool IsLightweight(IconSize size) {
        return size is IconSize.Small or IconSize.Normal;
    }


    /// <summary>
    /// Decodes bytes the icon cache already had and applies the image the
    /// same way a fresh load does — below input priority.
    /// </summary>
    private async Task DecodeAndApplyAsync(string path, IconSize size, byte[] bytes, int generation) {
        // Re-checked on the pool as well: a folder left behind has hundreds
        // of these queued, and decoding for a tile that is gone is pure
        // waste in front of the next folder's own work.
        var image = await Task.Run(() => generation == _generation ? IconImageCache.Get(path, size, bytes) : null)
            .ConfigureAwait(false);
        if (image is null || generation != _generation) {
            return;
        }

        _ = Dispatcher.BeginInvoke(ApplyPriority(size), () => {
            if (generation == _generation) {
                Source = image;
            }
        });
    }


    private async Task LoadAndApplyAsync(string path, IconSize size, int generation) {
        var image = await LoadAsync(path, size, () => generation == _generation).ConfigureAwait(false);

        // One more try before giving up. The list heals a failed icon by
        // itself — scrolling realises the container again and re-asks —
        // but the folder panels build their rows once per session, so a
        // load that failed there (the shell flaking under the startup
        // burst) stayed a blank row forever. Legitimate "no preview"
        // answers are negative-cached by the provider, so for those the
        // retry costs one dictionary lookup.
        if (image is null && generation == _generation) {
            await Task.Delay(RetryDelayMs).ConfigureAwait(false);
            if (generation == _generation) {
                image = await LoadAsync(path, size, () => generation == _generation).ConfigureAwait(false);
            }
        }

        // A load that is still wanted and got nothing is a provider failure
        // the retry above did not heal — the one line that told apart "the
        // shell flaked" from "the delivery was dropped" when tree icons
        // went missing. It fires only on genuine failures, so it can stay.
        // Superseded loads are routine and stay silent.
        if (image is null) {
            if (generation == _generation) {
                ServiceLocator.Get<Wander.Core.Logging.ILogger>().Info(
                    $"[icon-diag] no icon from provider ({size}) — {path}");
            }

            return;
        }
        if (generation != _generation) {
            return;
        }

        _ = Dispatcher.BeginInvoke(ApplyPriority(size), () => {
            if (generation == _generation) {
                Source = image;
            }
        });
    }


    /// <summary>
    /// The whole pipeline stays off the UI thread, and the finished image
    /// comes back at <see cref="DispatcherPriority.ContextIdle"/> — below
    /// input, and below the listing. A folder of photographs streams in
    /// dozens of thumbnails a second, and landing each one at the default
    /// (Normal) priority put that stream ahead of the user's clicks and
    /// keys in the dispatcher queue: the window went unresponsive for
    /// seconds while it was merely filling in pictures (ui.stall 2.5 s in
    /// the session log, with no navigation in flight at all). Background
    /// fixed that and made the next mistake: the rows of a new folder are
    /// cleared and landed at Background too, and the queue is FIFO, so a
    /// folder walked into from a folder of pictures showed the old rows
    /// filling in until every one of their pictures had landed. Pictures
    /// can wait; the user cannot, and neither can the folder they asked for.
    ///
    /// <para>See <see cref="IsLightweight"/> for why the two classes differ.</para>
    /// </summary>
    private static DispatcherPriority ApplyPriority(IconSize size) {
        return IsLightweight(size) ? DispatcherPriority.Normal : DispatcherPriority.ContextIdle;
    }

    private static async Task<BitmapImage?> LoadAsync(string path, IconSize size, Func<bool> stillWanted) {
        await _gate.WaitAsync().ConfigureAwait(false);
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
            }).ConfigureAwait(false);
        } catch {
            // A missing icon is a cosmetic loss; never let it reach the
            // dispatcher's unhandled-exception handler.
            return null;
        } finally {
            _gate.Release();
        }
    }
}
