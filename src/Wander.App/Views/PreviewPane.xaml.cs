using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Web.WebView2.Core;
using Wander.App.Controls;
using Wander.App.Resources;
using Wander.App.ViewModels;

namespace Wander.App.Views;

/// <summary>
/// Everything drawn inside the preview pane, and the three little state
/// machines behind it: the FastStone-style hold-RMB image zoom, the video
/// transport, and the WebView2 handshake for PDF / HTML / Markdown.
///
/// <para>
/// Split out of <see cref="MainWindow"/> because none of it talks to the
/// file list, the trees or the drop pipeline — it listens to
/// <see cref="PreviewController"/> and draws. The window keeps only the
/// layout question: whether the pane is shown and how wide it is.
/// </para>
/// </summary>
public partial class PreviewPane : UserControl {
    public PreviewPane() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }


    private MainViewModel Vm => (MainViewModel)DataContext;


    /// <summary>
    /// True while the keyboard is inside the code viewer. The window asks
    /// before claiming Ctrl+F: AvalonEdit owns that key for its own search
    /// panel, and stealing it there would be surprising.
    /// </summary>
    public bool IsCodeEditorFocused => CodeEditor.IsKeyboardFocusWithin;


    private bool _webInitialized;


    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.OldValue is MainViewModel old) {
            old.Preview.PropertyChanged -= OnPreviewPropertyChanged;
        }
        if (e.NewValue is MainViewModel vm) {
            vm.Preview.PropertyChanged += OnPreviewPropertyChanged;
            UpdateCodeEditor();
        }
    }


    private async void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(PreviewController.CodeText):
            case nameof(PreviewController.CodeExtension):
                UpdateCodeEditor();
                break;

            case nameof(PreviewController.WebUri):
                if (Vm.Preview.WebUri is { } uri) {
                    await EnsureWebViewReadyAsync();
                    try { WebPreview.Source = uri; } catch { /* webview not ready */ }
                }
                break;

            case nameof(PreviewController.WebHtml):
                if (Vm.Preview.WebHtml is { } html) {
                    await EnsureWebViewReadyAsync();
                    try { WebPreview.NavigateToString(html); } catch { /* webview not ready */ }
                }
                break;

            case nameof(PreviewController.Kind):
                // Bail out of any in-flight image-zoom state when the user
                // switches to a different file (e.g., RMB held when changing
                // selection). Also reset the video transport so a freshly
                // opened video starts paused with the play button correct.
                ExitImageZoom();
                ResetVideoTransport();
                break;

            case nameof(PreviewController.VideoUri):
                // MediaElement reloads on Source change via the binding; we
                // just reset the slider / play button so the UI matches.
                ResetVideoTransport();
                break;
        }
    }


    private void UpdateCodeEditor() {
        if (string.IsNullOrEmpty(Vm.Preview.CodeText)) {
            CodeEditor.Clear();
            CodeEditor.SyntaxHighlighting = null;
            return;
        }

        string ext = Vm.Preview.CodeExtension ?? "";
        // AvalonEdit ships highlighting for: C#, C++, Java, JS, TS, CSS, HTML, XML, JSON, Python, PHP, SQL, Markdown, ...
        CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        CodeEditor.Text = Vm.Preview.CodeText;
    }

    private async Task EnsureWebViewReadyAsync() {
        if (_webInitialized) {
            return;
        }
        try {
            // Explicit user-data folder: the default is "<exe dir>.WebView2",
            // which fails silently when Wander runs from a read-only location
            // (portable exe in Program Files, network share).
            string dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Wander", "WebView2");
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: dataFolder);
            await WebPreview.EnsureCoreWebView2Async(env);

            if (WebPreview.CoreWebView2 is { } core) {
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;

                // The pane renders untrusted local files (any .html the user
                // clicks), so lock it down: no autofill surfaces, no host
                // object / postMessage bridge into the app.
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsWebMessageEnabled = false;

                // No popups, and the pane itself may only display local
                // content: a previewed page must not be able to redirect the
                // preview (or the whole session, via window.open) to the web.
                core.NewWindowRequested += (_, args) => args.Handled = true;
                core.NavigationStarting += (_, args) => {
                    bool local = Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
                        && uri.Scheme is "file" or "about" or "data";
                    if (!local) {
                        args.Cancel = true;
                    }
                };

                // NavigationStarting only covers navigations. A previewed
                // .html could still reach the network through a tracking
                // pixel, a remote script or a fetch() — quietly telling
                // someone which files this machine looks at. Scripts stay
                // enabled because the built-in PDF viewer needs them, so the
                // subresources are where the line gets drawn instead.
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, args) => {
                    if (IsRemoteUri(args.Request.Uri)) {
                        args.Response = core.Environment.CreateWebResourceResponse(
                            null, 403, "Blocked", "");
                    }
                };
            }
            _webInitialized = true;
        } catch {
            // WebView2 runtime not installed — the pane will stay blank
            // for PDF / HTML / Markdown previews. Other previews are unaffected.
        }
    }


    /// <summary>
    /// Whether a request would leave this machine. Named the negative way
    /// round on purpose: the renderer serves its own chrome (the PDF viewer
    /// especially) over internal schemes we have no business enumerating, so
    /// the rule blocks what reaches the network rather than allowing a list
    /// of what doesn't.
    /// </summary>
    private static bool IsRemoteUri(string uri) {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https" or "ws" or "wss" or "ftp" or "ftps";
    }


    // ======================================================================
    // Preview pane: image zoom (FastStone-style RMB-hold pan zoom).
    // ======================================================================
    //
    // When the previewed image is downscaled to fit the pane:
    //   • the cursor turns into a magnifier glyph,
    //   • holding the right mouse button shows the image at native 1:1 with
    //     the pixel under the cursor anchored to the cursor's screen position,
    //   • moving the mouse pans the 1:1 view — release RMB to return.
    //
    // Geometry: as the cursor moves from (0,0) to (host.W, host.H) we map
    // linearly onto (0,0)..(src.W, src.H) image-pixel space, then position
    // the 1:1 image so that mapped pixel sits under the cursor. This
    // matches FastStone / IrfanView "navigator" zoom.

    private bool _imageZoomActive;

    private bool IsImageDownscaled() {
        if (ImgFit.Source is not BitmapSource src) {
            return false;
        }
        // A few pixels of slop avoid jitter exactly at break-even. If the
        // source is already smaller than the available render area there's
        // nothing useful to zoom into, so we don't switch the cursor.
        return src.PixelWidth > ImgFit.ActualWidth + 1
            || src.PixelHeight > ImgFit.ActualHeight + 1;
    }

    private void UpdateImageCursor() {
        ImagePreviewHost.Cursor = IsImageDownscaled() ? MagnifierCursor.Instance : null;
    }

    private void ImgFit_SizeChanged(object sender, SizeChangedEventArgs e) {
        // The fitted image's rendered size changes when the user resizes
        // the pane or selects a differently-sized image. Refresh the
        // cursor decision accordingly.
        UpdateImageCursor();
    }

    private void ImageZoom_MouseEnter(object sender, MouseEventArgs e) {
        UpdateImageCursor();
    }

    private void ImageZoom_MouseLeave(object sender, MouseEventArgs e) {
        // Don't kill an active zoom on Leave — Mouse.Capture means we keep
        // getting events anyway, and the user is probably panning to an
        // image edge. Just restore the cursor.
        if (!_imageZoomActive) {
            ImagePreviewHost.Cursor = null;
        }
    }

    private void ImageZoom_LmbDown(object sender, MouseButtonEventArgs e) {
        if (!IsImageDownscaled()) {
            return;
        }
        if (ImgFit.Source is not BitmapSource src) {
            return;
        }

        _imageZoomActive = true;
        // 1 DIP = 1 image pixel (no DPI compensation — matches FastStone's
        // "100 %" semantics on the user's currently configured DPI).
        ImgZoom.Width = src.PixelWidth;
        ImgZoom.Height = src.PixelHeight;
        ImgZoomCanvas.Visibility = Visibility.Visible;
        UpdateZoomPosition(e.GetPosition(ImagePreviewHost));
        // Capture so we still get the LMB-up if the user lifts the button
        // outside the host (e.g., over the splitter). LostMouseCapture is
        // our cleanup path.
        ImagePreviewHost.CaptureMouse();
        e.Handled = true;
    }

    private void ImageZoom_LmbUp(object sender, MouseButtonEventArgs e) {
        ExitImageZoom();
        e.Handled = true;
    }

    private void ImageZoom_MouseMove(object sender, MouseEventArgs e) {
        if (!_imageZoomActive) {
            return;
        }
        // Defensive: if LMB was released while we missed an event (e.g.,
        // capture got stolen), drop out of zoom.
        if (e.LeftButton != MouseButtonState.Pressed) {
            ExitImageZoom();
            return;
        }
        UpdateZoomPosition(e.GetPosition(ImagePreviewHost));
    }

    private void ImageZoom_LostCapture(object sender, MouseEventArgs e) {
        ExitImageZoom();
    }

    // Mirror of the Margin attribute on ImgFit (8,12,8,8) so the zoom view
    // can match the fit view's placement on the non-panning axis. Keep in
    // sync if the XAML margin ever changes.
    private const double PreviewImageMarginTop = 12;
    // Left/right margins are symmetric — the centering math (hw - srcW)/2
    // produces the same X whether you account for them or not, so no
    // constant needed for X.

    /// <summary>
    /// Positions the 1:1 zoom image so that the image-pixel under the
    /// cursor stays under the cursor. Pan is per-axis: only the dimension
    /// that doesn't fit the pane scrolls with the cursor. The other one
    /// is aligned to match how ImgFit (the fit-mode view) lays it out —
    /// centred horizontally, top-anchored vertically — so toggling zoom
    /// on doesn't visually jump the image to the middle.
    ///
    /// Mouse coordinates are clamped to the pane rectangle. The mouse
    /// capture during zoom lets the cursor travel outside the host (e.g.
    /// over the splitter); without clamping, the formula would extrapolate
    /// and shove the image past the edge it should be pinned to.
    /// </summary>
    private void UpdateZoomPosition(Point mouse) {
        if (ImgFit.Source is not BitmapSource src) {
            return;
        }
        double hw = ImagePreviewHost.ActualWidth;
        double hh = ImagePreviewHost.ActualHeight;
        if (hw <= 0 || hh <= 0) {
            return;
        }

        double srcW = src.PixelWidth;
        double srcH = src.PixelHeight;

        // Clamp to pane interior so leaving the pane doesn't scroll past
        // the image edges. At mouse.X == 0 we show the image's left edge;
        // at mouse.X == hw, the right edge.
        double mx = Math.Clamp(mouse.X, 0, hw);
        double my = Math.Clamp(mouse.Y, 0, hh);

        // X axis: pan only if image is wider than the pane.
        // When it fits, centre horizontally — matches ImgFit's
        // HorizontalAlignment="Center" with symmetric L/R margins.
        double x = srcW > hw
            ? mx - (mx / hw) * srcW
            : (hw - srcW) / 2;

        // Y axis: pan only if image is taller than the pane.
        // When it fits, anchor to the top with the same margin ImgFit
        // uses — ImgFit has VerticalAlignment="Top" + Margin="8,12,8,8",
        // so the fit view places the image at y=12. Centring vertically
        // here would visibly jump the image down when the user holds LMB.
        double y = srcH > hh
            ? my - (my / hh) * srcH
            : PreviewImageMarginTop;

        Canvas.SetLeft(ImgZoom, x);
        Canvas.SetTop(ImgZoom, y);
    }

    private void ExitImageZoom() {
        if (!_imageZoomActive) {
            return;
        }
        _imageZoomActive = false;
        ImgZoomCanvas.Visibility = Visibility.Collapsed;
        if (ImagePreviewHost.IsMouseCaptured) {
            ImagePreviewHost.ReleaseMouseCapture();
        }
        UpdateImageCursor();
    }


    // ======================================================================
    // Preview pane: video transport (MediaElement + Play/Pause + seek).
    // ======================================================================

    private DispatcherTimer? _videoTimer;
    private bool _videoIsPlaying;
    private bool _videoSliderDragging;
    private bool _suppressVideoSliderChanged;

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e) {
        // Cap the video preview to native pixel size — same rationale as
        // for images: a 320×240 clip shouldn't stretch to fill a giant
        // preview pane. Done here because NaturalVideoWidth/Height aren't
        // known until MediaElement has actually opened the file.
        if (VideoPreview.NaturalVideoWidth > 0 && VideoPreview.NaturalVideoHeight > 0) {
            VideoPreview.MaxWidth = VideoPreview.NaturalVideoWidth;
            VideoPreview.MaxHeight = VideoPreview.NaturalVideoHeight;
        } else {
            VideoPreview.MaxWidth = double.PositiveInfinity;
            VideoPreview.MaxHeight = double.PositiveInfinity;
        }

        if (!VideoPreview.NaturalDuration.HasTimeSpan) {
            return;
        }
        double total = VideoPreview.NaturalDuration.TimeSpan.TotalSeconds;
        _suppressVideoSliderChanged = true;
        VideoSlider.Maximum = total;
        VideoSlider.Value = 0;
        _suppressVideoSliderChanged = false;

        UpdateVideoTimeText();
        EnsureVideoTimer();
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e) {
        // Rewind to start, leave paused — same convention as Explorer's
        // preview pane and most desktop video viewers.
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Pause();
        _videoIsPlaying = false;
        VideoPlayPauseButton.Content = "▶";
    }

    private void VideoPreview_MediaFailed(object sender, ExceptionRoutedEventArgs e) {
        // Codec not installed (e.g. .webm without the Web Media Extensions)
        // or corrupt file. Surface a minimal hint in the slider area.
        VideoTimeText.Text = Strings.PreviewVideoUnavailable;
    }

    private void EnsureVideoTimer() {
        if (_videoTimer is not null) {
            return;
        }
        // 200 ms is responsive enough for a progress bar and cheap on CPU.
        _videoTimer = new DispatcherTimer(DispatcherPriority.Background) {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _videoTimer.Tick += VideoTimer_Tick;
        _videoTimer.Start();
    }

    private void VideoTimer_Tick(object? sender, EventArgs e) {
        if (_videoSliderDragging) {
            return;
        }
        if (!VideoPreview.NaturalDuration.HasTimeSpan) {
            return;
        }
        // Avoid feedback: setting Slider.Value programmatically would
        // otherwise re-fire ValueChanged and try to seek us back.
        _suppressVideoSliderChanged = true;
        VideoSlider.Value = VideoPreview.Position.TotalSeconds;
        _suppressVideoSliderChanged = false;
        UpdateVideoTimeText();
    }

    private void VideoPlayPause_Click(object sender, RoutedEventArgs e) {
        if (_videoIsPlaying) {
            VideoPreview.Pause();
            _videoIsPlaying = false;
            VideoPlayPauseButton.Content = "▶";
        } else {
            VideoPreview.Play();
            _videoIsPlaying = true;
            VideoPlayPauseButton.Content = "⏸";
        }
    }

    private void VideoSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
        _videoSliderDragging = true;
    }

    private void VideoSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
        _videoSliderDragging = false;
        // Final seek to the slider's resting value — ValueChanged during the
        // drag already kept Position roughly synced with ScrubbingEnabled,
        // but a final commit handles the last pointer position cleanly.
        if (VideoPreview.NaturalDuration.HasTimeSpan) {
            VideoPreview.Position = TimeSpan.FromSeconds(VideoSlider.Value);
            UpdateVideoTimeText();
        }
    }

    private void VideoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (_suppressVideoSliderChanged) {
            return;
        }
        if (!VideoPreview.NaturalDuration.HasTimeSpan) {
            return;
        }
        // ScrubbingEnabled lets MediaElement show frames while we seek
        // mid-drag, so we apply Position on every tick — feels responsive.
        VideoPreview.Position = TimeSpan.FromSeconds(e.NewValue);
        UpdateVideoTimeText();
    }

    private void UpdateVideoTimeText() {
        TimeSpan pos = VideoPreview.Position;
        TimeSpan dur = VideoPreview.NaturalDuration.HasTimeSpan
            ? VideoPreview.NaturalDuration.TimeSpan
            : TimeSpan.Zero;
        VideoTimeText.Text = $"{FormatTimecode(pos)} / {FormatTimecode(dur)}";
    }

    private static string FormatTimecode(TimeSpan t) {
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }

    private void ResetVideoTransport() {
        // Explicitly pause: WPF's Visibility=Collapsed doesn't tear the
        // MediaElement down, so audio would otherwise keep playing in the
        // background after the user selects another file.
        try { VideoPreview.Pause(); } catch { /* not yet loaded */ }
        _videoIsPlaying = false;
        VideoPlayPauseButton.Content = "▶";
        _suppressVideoSliderChanged = true;
        try {
            VideoSlider.Value = 0;
            VideoSlider.Maximum = 1;
        } finally {
            _suppressVideoSliderChanged = false;
        }
        VideoTimeText.Text = "0:00 / 0:00";
        // Drop the native-size cap so a fresh video isn't constrained by
        // the previous clip's resolution until MediaOpened reconfigures it.
        VideoPreview.MaxWidth = double.PositiveInfinity;
        VideoPreview.MaxHeight = double.PositiveInfinity;
    }
}
