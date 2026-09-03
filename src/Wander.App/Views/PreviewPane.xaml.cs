using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Web.WebView2.Core;
using Wander.App.Controllers;
using Wander.App.Controls;
using Wander.App.Highlighting;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core.Persistence;
using Wander.Core.Preview;


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
    private bool _webInitialized;

    public PreviewPane() {
        InitializeComponent();
        // Wander's own .xshd definitions (batch, ShaderLab, YAML) have to be
        // in the manager before the first file asks for one.
        HighlightingCatalog.EnsureRegistered();
        DataContextChanged += OnDataContextChanged;

        // The audio player has the same three events as the MediaElement,
        // just not as routed ones, so they are hooked here rather than in
        // XAML and land in the same handlers.
        _audioPlayer.MediaOpened += (_, _) => MediaOpened();
        _audioPlayer.MediaEnded += (_, _) => MediaEnded();
        _audioPlayer.MediaFailed += (_, _) => VideoTimeText.Text = Strings.PreviewVideoUnavailable;
    }


    private MainViewModel Vm => (MainViewModel)DataContext;


    /// <summary>
    /// True while the keyboard is inside the code viewer. The window asks
    /// before claiming Ctrl+F: AvalonEdit owns that key for its own search
    /// panel, and stealing it there would be surprising.
    /// </summary>
    public bool IsCodeEditorFocused => CodeEditor.IsKeyboardFocusWithin;


    /// <summary>
    /// Copies whatever text is selected in the pane, and says how much.
    /// Returns null when the keyboard is not in here, or is but has nothing
    /// selected — then Ctrl+C means the files, as it always did.
    ///
    /// <para>
    /// The window asks before its own Ctrl+C runs, rather than leaving each
    /// text control to answer for itself. Two reasons: the answer has to be
    /// the same in all four of them (plain text, code, rich text and the
    /// GUID box), and the user has to be told which of the two things
    /// Ctrl+C did — the pane and the list are one keystroke apart, and
    /// "copied 3 items" over a selected paragraph is a silent wrong answer.
    /// The web view is left alone: it is a browser and copies for itself,
    /// out of a document we cannot read.
    /// </para>
    /// </summary>
    public int? TryCopySelectedText() {
        if (!IsKeyboardFocusWithin) {
            return null;
        }

        switch (Keyboard.FocusedElement) {
            case TextBox { SelectionLength: > 0 } box:
                box.Copy();

                return box.SelectionLength;

            case RichTextBox rich when !rich.Selection.IsEmpty:
                int length = rich.Selection.Text.Length;
                rich.Copy();

                return length;

            default:
                // AvalonEdit's editor is not the focused element - its own
                // text area is - so it is asked directly rather than
                // matched on.
                if (CodeEditor.IsKeyboardFocusWithin && CodeEditor.SelectionLength > 0) {
                    CodeEditor.Copy();

                    return CodeEditor.SelectionLength;
                }

                return null;
        }
    }


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

            case nameof(PreviewController.DocumentPath):
                await LoadDocumentAsync(Vm.Preview.DocumentPath);
                break;

            case nameof(PreviewController.Kind):
                // Bail out of any in-flight image-zoom state when the user
                // switches to a different file (e.g., RMB held when changing
                // selection). Also reset the video transport so a freshly
                // opened video starts paused with the play button correct.
                ExitImageZoom();
                ResetVideoTransport();
                ResetModelView();
                break;

            case nameof(PreviewController.MediaUri):
                // The controller sets Kind before MediaUri precisely so
                // that this can tell a track from a clip.
                OpenMedia(Vm.Preview.MediaUri);
                break;

            case nameof(PreviewController.ModelParts):
                ShowModel();
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

    /// <summary>
    /// Fills the rich-text viewer from an <c>.rtf</c> file. WPF's own RTF
    /// reader does the work — the format has been in the framework since
    /// the beginning — so this is a read off the disk and a handover.
    ///
    /// <para>
    /// The bytes are pulled on a worker thread and parsed on the UI one:
    /// <c>TextRange.Load</c> builds a <c>FlowDocument</c>, which is a
    /// DispatcherObject and cannot be built anywhere else. Parsing an RTF
    /// is fast; waiting on a sleeping disk is not, and that half is what
    /// gets moved off.
    /// </para>
    /// </summary>
    private async Task LoadDocumentAsync(string? path) {
        if (string.IsNullOrEmpty(path)) {
            DocumentPreview.Document = new FlowDocument();

            return;
        }

        byte[] bytes;
        try {
            bytes = await File.ReadAllBytesAsync(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            DocumentPreview.Document = new FlowDocument();

            return;
        }

        // The selection may have moved on while the file was being read.
        if (!string.Equals(Vm.Preview.DocumentPath, path, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        var document = new FlowDocument();
        try {
            using var stream = new MemoryStream(bytes);
            var range = new TextRange(document.ContentStart, document.ContentEnd);
            range.Load(stream, DataFormats.Rtf);
        } catch (ArgumentException) {
            // Not actually RTF, or RTF the reader refuses. An empty page
            // says that better than a half-parsed one.
            document = new FlowDocument();
        }

        DocumentPreview.Document = document;
    }


    private async Task EnsureWebViewReadyAsync() {
        if (_webInitialized) {
            return;
        }
        try {
            // Explicit user-data folder: the default is "<exe dir>.WebView2",
            // which fails silently when Wander runs from a read-only location
            // (portable exe in Program Files, network share).
            string dataFolder = AppPaths.WebView2;
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


    // --- Image zoom (FastStone-style RMB-hold pan) ----------------------
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

    // Mirror of the Margin attribute on ImgFit (8,12,8,8) so the zoom view
    // can match the fit view's placement on the non-panning axis. Keep in
    // sync if the XAML margin ever changes.
    private const double PreviewImageMarginTop = 12;
    // Left/right margins are symmetric — the centering math (hw - srcW)/2
    // produces the same X whether you account for them or not, so no
    // constant needed for X.

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


    // --- Video transport (MediaElement + Play/Pause + seek) -------------

    private DispatcherTimer? _videoTimer;
    private bool _videoIsPlaying;
    private bool _videoSliderDragging;
    private bool _suppressVideoSliderChanged;

    /// <summary>
    /// What plays a music file. Not the <see cref="MediaElement"/> above
    /// it: that one only works while it is being drawn, and a track has
    /// nothing to draw. <see cref="MediaPlayer"/> is the same engine
    /// without the element, which is exactly what audio needs.
    /// </summary>
    private readonly MediaPlayer _audioPlayer = new();

    /// <summary>Which of the two the transport is driving right now.</summary>
    private bool _transportIsAudio;

    /// <summary>
    /// Notices a clip finishing when the player does not say so — see
    /// <see cref="PlaybackClock"/>. Fed by the same 200 ms tick that moves
    /// the seek bar.
    /// </summary>
    private readonly PlaybackClock _clock = new();

    /// <summary>What the transport has open, for <see cref="RestartMedia"/> to open again.</summary>
    private Uri? _mediaUri;

    /// <summary>
    /// True between a restart and the <c>MediaOpened</c> it causes: the
    /// same file coming back, not a new one.
    /// </summary>
    private bool _restarting;

    /// <summary>
    /// The file has played to its end and is waiting to be started over.
    ///
    /// <para>
    /// Kept as a flag rather than read off the position, because the
    /// position is no help: the player rewinds itself to zero when it
    /// ends, so "am I at the end" answers no the moment the clip is over.
    /// That is what left the button dead after the first play — measured,
    /// not guessed.
    /// </para>
    /// </summary>
    private bool _finished;

    /// <summary>
    /// The furthest the position has been seen to reach. Stands in for the
    /// length of a file that declares none — after one play it is the only
    /// honest number the clock has, and "0:00" for a clip that plainly
    /// played is worse than an estimate rounded up to "0:01".
    /// </summary>
    private TimeSpan _seen;


    // --- one transport, two players -------------------------------------

    private TimeSpan TransportPosition {
        get => _transportIsAudio ? _audioPlayer.Position : VideoPreview.Position;
        set {
            if (_transportIsAudio) {
                _audioPlayer.Position = value;
            } else {
                VideoPreview.Position = value;
            }
        }
    }

    /// <summary>How long the track or clip runs, or null while that is still unknown.</summary>
    private TimeSpan? TransportDuration {
        get {
            var duration = _transportIsAudio ? _audioPlayer.NaturalDuration : VideoPreview.NaturalDuration;

            return duration.HasTimeSpan ? duration.TimeSpan : null;
        }
    }

    private void TransportPlay() {
        if (_transportIsAudio) {
            _audioPlayer.Play();
        } else {
            VideoPreview.Play();
        }
    }

    private void TransportPause() {
        if (_transportIsAudio) {
            _audioPlayer.Pause();
        } else {
            VideoPreview.Pause();
        }
    }

    /// <summary>
    /// Plays the current file from its beginning.
    ///
    /// <para>
    /// Two roads, chosen by whether the file declares its length. One that
    /// does is rewound - <c>Position = 0; Play()</c> - and the picture on
    /// screen stays until the first frame of the next pass. One that does
    /// not cannot be: <c>burn-in-hell-elmo.mp4</c> (0.8 s, no length in the
    /// container), measured on a MediaPlayer stand (2026-09-03) - after it
    /// ends, <c>Position = 0; Play()</c> and <c>Stop(); Play()</c> both
    /// leave the position at zero, and only <c>Close()</c> followed by a
    /// fresh open plays it again; a file with a length took all three. The
    /// re-open costs a frame of nothing between the two, so it is kept for
    /// the files that need it, and <see cref="HoldLastFrame"/> covers that
    /// frame.
    /// </para>
    /// </summary>
    private void RestartMedia() {
        if (_mediaUri is not { } uri) {
            return;
        }

        _finished = false;
        _clock.Reset();

        if (TransportDuration is not null) {
            TransportPosition = TimeSpan.Zero;
            TransportPlay();
        } else {
            // The re-open raises MediaOpened again, and that handler must
            // not treat this as a new file: it would recompute the repeat
            // button and undo whatever the user had chosen.
            _restarting = true;
            if (_transportIsAudio) {
                _audioPlayer.Close();
                _audioPlayer.Open(uri);
                _audioPlayer.Play();
            } else {
                HoldLastFrame();
                VideoPreview.Close();
                VideoPreview.Source = uri;
                VideoPreview.Play();
            }
        }

        _videoIsPlaying = true;
        VideoPlayPauseButton.Content = "⏸";
        EnsureVideoTimer();
    }

    /// <summary>
    /// Back to the start, stopped rather than paused: the state a clip that
    /// played to its end waits in until the button is pressed. Stop()
    /// rewinds by itself; whether Play() then moves the file is
    /// <see cref="RestartMedia"/>'s question, not this one's.
    /// </summary>
    private void TransportRewind() {
        if (_transportIsAudio) {
            _audioPlayer.Stop();
        } else {
            VideoPreview.Stop();
        }
    }

    /// <summary>
    /// Covers the video element with a snapshot of the frame it is showing,
    /// and holds the element at its size, for as long as reopening the file
    /// takes. Closed, a MediaElement has no natural size and measures to
    /// nothing; opened, it draws black until the first frame is decoded -
    /// on a 0.8 s clip with repeat on that was a collapse of the picture
    /// and a black flash every second. Released by the first tick that
    /// sees the position move (<see cref="VideoTimer_Tick"/>) and by any
    /// new file (<see cref="ResetVideoTransport"/>).
    /// </summary>
    private void HoldLastFrame() {
        double width = VideoPreview.ActualWidth;
        double height = VideoPreview.ActualHeight;
        if (width < 1 || height < 1) {
            return;
        }

        // Through a DrawingVisual rather than Render(VideoPreview): a
        // visual rendered directly is drawn at its own offset inside its
        // parent - here the margin - which leaves a blank strip and crops
        // the picture. Pixel size follows the monitor so the snapshot is
        // not softer than the frame it stands in for.
        var dpi = VisualTreeHelper.GetDpi(VideoPreview);
        var frame = new RenderTargetBitmap(
            (int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) {
            dc.DrawRectangle(new VisualBrush(VideoPreview), null, new Rect(0, 0, width, height));
        }
        frame.Render(visual);

        VideoHold.Source = frame;
        VideoHold.Width = width;
        VideoHold.Height = height;
        VideoHold.Visibility = Visibility.Visible;
        VideoPreview.MinWidth = width;
        VideoPreview.MinHeight = height;
    }

    private void ReleaseHeldFrame() {
        if (VideoHold.Visibility != Visibility.Visible) {
            return;
        }

        VideoHold.Visibility = Visibility.Collapsed;
        VideoHold.Source = null;
        VideoPreview.MinWidth = 0;
        VideoPreview.MinHeight = 0;
    }

    /// <summary>
    /// Points the transport at a new file. Both players are stopped first:
    /// walking from a clip to a track and back must not leave the previous
    /// one running behind the new one.
    /// </summary>
    private void OpenMedia(Uri? uri) {
        try { VideoPreview.Stop(); } catch { /* not yet loaded */ }
        try { _audioPlayer.Stop(); } catch { /* nothing open */ }

        _mediaUri = uri;
        _restarting = false;
        _finished = false;
        _seen = TimeSpan.Zero;
        _transportIsAudio = Vm.Preview.Kind == PreviewKind.Audio;

        if (uri is null) {
            VideoPreview.Source = null;
            _audioPlayer.Close();
        } else if (_transportIsAudio) {
            VideoPreview.Source = null;
            _audioPlayer.Open(uri);
        } else {
            _audioPlayer.Close();
            VideoPreview.Source = uri;
        }

        ResetVideoTransport();
    }

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

        MediaOpened();
    }

    /// <summary>
    /// A file has opened and its length is known: size the seek bar to it
    /// and start the clock. Shared, because "how long is this" is the same
    /// question for a clip and for a track.
    /// </summary>
    private void MediaOpened() {
        var natural = TransportDuration;
        _clock.Reset();

        // The same file coming back from RestartMedia: the repeat button is
        // the user's choice by now, and recomputing it here would undo it
        // on every loop.
        bool restarted = _restarting;
        _restarting = false;

        // A very short clip is unreadable played once — by the time the eye
        // has found it, it is over — so repeat starts on for those and off
        // for everything else. Set per file rather than remembered: the
        // answer belongs to the clip, not to the session. A length the
        // container does not declare counts as short (PlaybackClock) —
        // those are the two-second clips. Sound is left alone; a
        // two-second noise on a loop is not a preview, it is an alarm.
        if (!restarted) {
            VideoLoopButton.IsChecked = !_transportIsAudio && PlaybackClock.LoopsByDefault(natural);
        }

        // A file whose length is not known yet still gets a clock: without
        // one, nothing watches its position, and a clip that never raises
        // "ended" would leave the transport claiming to play forever.
        if (natural is not { } total) {
            UpdateVideoTimeText();
            EnsureVideoTimer();

            return;
        }

        _suppressVideoSliderChanged = true;
        VideoSlider.Maximum = total.TotalSeconds;
        VideoSlider.Value = 0;
        _suppressVideoSliderChanged = false;

        UpdateVideoTimeText();
        EnsureVideoTimer();
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e) {
        MediaEnded();
    }

    /// <summary>
    /// With repeat on, start over; otherwise rewind and stop — the same
    /// convention as Explorer's preview pane and most desktop video
    /// viewers. Starting over means opening the file again, not rewinding:
    /// see <see cref="RestartMedia"/>.
    /// </summary>
    private void MediaEnded() {
        _clock.Reset();
        if (VideoLoopButton.IsChecked == true) {
            RestartMedia();
            UpdateVideoTimeText();

            return;
        }

        TransportRewind();
        _videoIsPlaying = false;
        _finished = true;
        VideoPlayPauseButton.Content = "▶";
        UpdateVideoTimeText();
    }

    private void VideoPreview_MediaFailed(object sender, ExceptionRoutedEventArgs e) {
        // Codec not installed (e.g. .webm without the Web Media Extensions)
        // or corrupt file. Surface a minimal hint in the slider area.
        ReleaseHeldFrame();
        VideoTimeText.Text = Strings.PreviewVideoUnavailable;
    }

    private void EnsureVideoTimer() {
        if (_videoTimer is null) {
            // 200 ms is responsive enough for a progress bar and cheap on CPU.
            _videoTimer = new DispatcherTimer(DispatcherPriority.Background) {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _videoTimer.Tick += VideoTimer_Tick;
        }

        // Restart as well as create: ResetVideoTransport stops the clock
        // whenever the transport lets go of a file, so every media open
        // has to be able to wind it up again.
        _videoTimer.Start();
    }

    private void VideoTimer_Tick(object? sender, EventArgs e) {
        if (_videoSliderDragging) {
            return;
        }

        var position = TransportPosition;
        var duration = TransportDuration;
        if (position > _seen) {
            _seen = position;
        }
        if (position > TimeSpan.Zero) {
            // The reopened file is drawing frames again; the snapshot that
            // stood in for it can go.
            ReleaseHeldFrame();
        }
        if (duration is not null) {
            // Avoid feedback: setting Slider.Value programmatically would
            // otherwise re-fire ValueChanged and try to seek us back.
            _suppressVideoSliderChanged = true;
            VideoSlider.Value = position.TotalSeconds;
            _suppressVideoSliderChanged = false;
        }
        UpdateVideoTimeText();

        // Some files play to their last frame and raise nothing. Then this
        // is what ends the playback: the button goes back to "play", and
        // repeat gets its chance — see PlaybackClock.
        if (_clock.NoteTick(position, duration, _videoIsPlaying)) {
            MediaEnded();
        }
    }

    private void VideoPlayPause_Click(object sender, RoutedEventArgs e) {
        if (_videoIsPlaying) {
            TransportPause();
            _videoIsPlaying = false;
            VideoPlayPauseButton.Content = "▶";
        } else if (_finished || PlaybackClock.AtEnd(TransportPosition, TransportDuration)) {
            // A file that has finished cannot be played from where it
            // stands - and how it is taken back to the start depends on the
            // file, which is RestartMedia's business. The flag is checked
            // first because the player rewinds itself on ending, so the
            // position alone would say "not at the end".
            RestartMedia();
        } else {
            _clock.Reset();
            TransportPlay();
            _videoIsPlaying = true;
            VideoPlayPauseButton.Content = "⏸";
            // The clock only starts once something is playing; for a track
            // opened and played straight away, MediaOpened may already have
            // been and gone.
            EnsureVideoTimer();
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
        if (TransportDuration is not null) {
            TransportPosition = TimeSpan.FromSeconds(VideoSlider.Value);
            UpdateVideoTimeText();
        }
    }

    private void VideoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (_suppressVideoSliderChanged) {
            return;
        }
        if (TransportDuration is null) {
            return;
        }
        // ScrubbingEnabled lets MediaElement show frames while we seek
        // mid-drag, so we apply Position on every tick — feels responsive.
        TransportPosition = TimeSpan.FromSeconds(e.NewValue);
        UpdateVideoTimeText();
    }

    private void UpdateVideoTimeText() {
        // The length rounds up, the position down: a clip of 0.8 s that
        // truncated to "0:00" read as an empty file (see Timecode). And
        // when the file declares no length at all — burn-in-hell-elmo.mp4
        // does not — the furthest the position has reached stands in for
        // it, so after one play the clock says "0:01" instead of nothing.
        VideoTimeText.Text =
            $"{Timecode.Format(TransportPosition)} / {Timecode.Format(TransportDuration ?? _seen, roundUp: true)}";
    }

    private void ResetVideoTransport() {
        // The clock has nothing to track until the next MediaOpened winds
        // it up again - without this it kept ticking for the rest of the
        // session, text files and hidden pane included (TECHDEBT, closed
        // 2026-09-01).
        _videoTimer?.Stop();
        // Explicitly pause: WPF's Visibility=Collapsed doesn't tear the
        // MediaElement down, so audio would otherwise keep playing in the
        // background after the user selects another file.
        try { VideoPreview.Pause(); } catch { /* not yet loaded */ }
        try { _audioPlayer.Pause(); } catch { /* nothing open */ }
        ReleaseHeldFrame();
        _clock.Reset();
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


    // --- The 3D viewport (camera framing + orbit + zoom) ----------------

    /// <summary>
    /// Slack around the model once it has been fitted to the pane. Ten per
    /// cent: enough that the silhouette does not touch the edges, not so
    /// much that the model sits in a field of grey.
    /// </summary>
    private const double ModelFitMargin = 1.1;

    private const double ModelMinZoom = 0.3;
    private const double ModelMaxZoom = 8.0;

    private Point _modelDragFrom;
    private bool _modelDragging;
    private double _modelZoom = 1.0;

    /// <summary>
    /// Points the camera at the model that has just been read.
    ///
    /// <para>
    /// Framing cannot be done in XAML because it depends on the file: a
    /// printer part is tens of millimetres across and a scanned building is
    /// tens of metres, and a camera placed at a fixed distance shows one of
    /// them as a dot and puts the other one behind it. Everything here is
    /// in units of the model's own radius, so both frame the same.
    /// </para>
    /// </summary>
    private void ShowModel() {
        ModelParts.Children.Clear();
        foreach (var part in Vm.Preview.ModelParts) {
            ModelParts.Children.Add(new GeometryModel3D {
                Geometry = part.Geometry,
                Material = new DiffuseMaterial(part.Front),
                BackMaterial = new DiffuseMaterial(part.Back),
            });
        }

        if (ModelParts.Children.Count == 0) {
            return;
        }

        ResetModelView();
    }

    private void ResetModelView() {
        _modelDragging = false;
        _modelZoom = 1.0;
        ModelSpin.Angle = 0;
        ModelTilt.Angle = 0;
        PlaceModelCamera();
    }

    private void PlaceModelCamera() {
        if (!Vm.Preview.HasModel) {
            return;
        }

        var centre = Vm.Preview.ModelCenter;
        double radius = Vm.Preview.ModelRadius;
        double distance = FitDistance(radius) * _modelZoom;

        // Down the Z axis and slightly above, which is the three-quarter
        // view every modelling tool opens on — a model seen dead-on
        // reads as a flat silhouette.
        var offset = new Vector3D(0, radius * 0.55, distance);

        ModelCamera.Position = centre + offset;
        ModelCamera.LookDirection = -offset;
        ModelCamera.UpDirection = new Vector3D(0, 1, 0);

        // The clip planes travel with the model too: leaving them at their
        // defaults makes anything much smaller than a metre disappear into
        // the near plane.
        ModelCamera.NearPlaneDistance = Math.Max(radius * 0.01, 1e-5);
        ModelCamera.FarPlaneDistance = (distance + (radius * 4)) * 4;
    }

    /// <summary>
    /// How far the camera has to stand for a sphere of
    /// <paramref name="radius"/> to fit inside the frame.
    ///
    /// <para>
    /// Worked out rather than guessed at, because a guess is wrong in one
    /// direction or the other for every pane width. WPF states
    /// <c>FieldOfView</c> horizontally, so the vertical angle depends on
    /// the pane's shape — and a preview pane is a tall narrow strip, where
    /// the vertical angle is the tight one. Fitting against the wider of
    /// the two is what let a cube hang off the top and bottom edges.
    /// </para>
    /// </summary>
    private double FitDistance(double radius) {
        double width = ModelViewport.ActualWidth;
        double height = ModelViewport.ActualHeight;

        // Before the first layout pass there is no shape to fit to; 4:3 is
        // as good a guess as any, and the next resize corrects it.
        double aspect = width > 0 && height > 0 ? height / width : 0.75;

        double halfHorizontal = ModelCamera.FieldOfView / 2 * Math.PI / 180;
        double halfVertical = Math.Atan(Math.Tan(halfHorizontal) * aspect);
        double tight = Math.Max(Math.Min(halfHorizontal, halfVertical), 0.01);

        return radius / Math.Sin(tight) * ModelFitMargin;
    }

    /// <summary>
    /// The model spins about its own centre, not the origin — the two are
    /// rarely the same, and rotating about the origin swings a model that
    /// sits away from it right out of frame.
    /// </summary>
    private void ApplyModelRotationCentre() {
        var centre = Vm.Preview.ModelCenter;
        ModelSpin.Axis = new Vector3D(0, 1, 0);
        ModelTilt.Axis = new Vector3D(1, 0, 0);

        if (ModelParts.Transform is Transform3DGroup group) {
            foreach (var transform in group.Children) {
                if (transform is RotateTransform3D rotate) {
                    rotate.CenterX = centre.X;
                    rotate.CenterY = centre.Y;
                    rotate.CenterZ = centre.Z;
                }
            }
        }
    }

    private void Model_MouseDown(object sender, MouseButtonEventArgs e) {
        if (!Vm.Preview.HasModel) {
            return;
        }

        _modelDragging = true;
        _modelDragFrom = e.GetPosition((IInputElement)sender);
        ApplyModelRotationCentre();
        ((UIElement)sender).CaptureMouse();
    }

    private void Model_MouseUp(object sender, MouseButtonEventArgs e) {
        _modelDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void Model_MouseMove(object sender, MouseEventArgs e) {
        if (!_modelDragging) {
            return;
        }

        var now = e.GetPosition((IInputElement)sender);
        ModelSpin.Angle += (now.X - _modelDragFrom.X) * 0.4;

        // Clamped rather than free: past the poles the model appears
        // upside down and every further drag moves it the wrong way.
        ModelTilt.Angle = Math.Clamp(ModelTilt.Angle + ((now.Y - _modelDragFrom.Y) * 0.4), -89, 89);
        _modelDragFrom = now;
    }

    /// <summary>
    /// Re-fits on resize. The camera distance is derived from the pane's
    /// shape, so dragging the splitter narrower has to move the camera or
    /// the model starts overflowing the sides.
    /// </summary>
    private void Model_SizeChanged(object sender, SizeChangedEventArgs e) {
        PlaceModelCamera();
    }

    private void Model_MouseWheel(object sender, MouseWheelEventArgs e) {
        if (!Vm.Preview.HasModel) {
            return;
        }

        _modelZoom = Math.Clamp(_modelZoom * (e.Delta > 0 ? 0.85 : 1.0 / 0.85), ModelMinZoom, ModelMaxZoom);
        PlaceModelCamera();
        e.Handled = true;
    }
}
