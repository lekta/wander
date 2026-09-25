using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
using Wander.Core.FileSystem;
using Wander.Core.Persistence;
using Wander.Core.Preview;


namespace Wander.App.Views;

/// <summary>
/// Everything drawn inside the preview pane, and the three little state
/// machines behind it: the FastStone-style hold-the-button image zoom, the video
/// transport, and the WebView2 handshake for PDF / HTML / Markdown.
///
/// <para>
/// Split out of <see cref="MainWindow"/> because none of it talks to the
/// file list, the trees or the drop pipeline — it listens to
/// <see cref="PreviewController"/> and draws. The window keeps only the
/// layout question: whether the pane is shown and how wide it is.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1001",
    Justification = "The count source is cancelled when the query changes or the matches are forgotten; managed only, nothing to release; the pane lives as long as its window.")]
public partial class PreviewPane : UserControl {
    private bool _webInitialized;

    // A scroll made to follow the other text of a comparison - not the user's.
    private bool _followingScroll;

    // Ctrl+3 came while the content was still on its way: it takes the
    // keyboard when it comes (TakeKeyboard).
    private bool _keyboardWanted;

    public PreviewPane() {
        InitializeComponent();
        // Wander's own .xshd definitions (batch, ShaderLab, YAML) have to be
        // in the manager before the first file asks for one.
        HighlightingCatalog.EnsureRegistered();
        PictureControls.Opacity = BarAtRest;
        PictureChartFace.Opacity = ChartAtRest;
        // A pane put away with the mouse by its bar - a pair full screen
        // down to one picture - is by it no longer, and says so to the other.
        IsVisibleChanged += (_, _) => {
            if (!IsVisible) {
                ReachBar(false);
            }
        };
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => {
            DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            ReportViewport();
        };

        // The audio player has the same three events as the MediaElement,
        // just not as routed ones, so they are hooked here rather than in
        // XAML and land in the same handlers.
        _audioPlayer.MediaOpened += (_, _) => MediaOpened();
        _audioPlayer.MediaEnded += (_, _) => MediaEnded();
        _audioPlayer.MediaFailed += (_, _) => VideoTimeText.Text = Strings.PreviewVideoUnavailable;
    }


    /// <summary>
    /// What this pane draws. The controller is the DataContext itself, not
    /// a property of the window's view model: the same control then works
    /// for the second half of a split pane, or in a window of its own, with
    /// nothing but a different controller handed to it.
    /// </summary>
    private PreviewController Controller => (PreviewController)DataContext;

    public static readonly DependencyProperty DpiScaleProperty = DependencyProperty.Register(
        nameof(DpiScale), typeof(double), typeof(PreviewPane), new PropertyMetadata(1.0));

    /// <summary>
    /// Device pixels per layout unit on the monitor the pane is on - what
    /// the pictures' caps divide their pixels by (PLAN AM). A property to
    /// bind to, so a move to another monitor redoes them.
    /// </summary>
    public double DpiScale {
        get => (double)GetValue(DpiScaleProperty);
        private set => SetValue(DpiScaleProperty, value);
    }

    public static readonly DependencyProperty PictureMarginProperty = DependencyProperty.Register(
        nameof(PictureMargin), typeof(Thickness), typeof(PreviewPane),
        new PropertyMetadata(new Thickness(4), (d, _) => ((PreviewPane)d).ReportViewport()));

    /// <summary>
    /// The room between a picture and the pane's edges (2026-09-24): thin in
    /// the pane, none full screen - there the picture meets the screen's
    /// edges. The fitted picture, its helpers' marks and an animation keep
    /// it; the 1:1 zoom and the decode size read it (UpdateZoomPosition,
    /// ReportViewport).
    /// </summary>
    public Thickness PictureMargin {
        get => (Thickness)GetValue(PictureMarginProperty);
        set => SetValue(PictureMarginProperty, value);
    }

    public static readonly DependencyProperty BarAtRestProperty = DependencyProperty.Register(
        nameof(BarAtRest), typeof(double), typeof(PreviewPane),
        new PropertyMetadata(0.3, (d, _) => ((PreviewPane)d).RestBar()));

    /// <summary>
    /// How much of the picture's bar shows while the mouse is away from it
    /// and no rating is being shown: a trace in the pane, 30%; none full
    /// screen (2026-09-24), where it comes up only when called for, as in
    /// FastStone - the mouse at the bottom edge, a rating key.
    /// </summary>
    public double BarAtRest {
        get => (double)GetValue(BarAtRestProperty);
        set => SetValue(BarAtRestProperty, value);
    }


    /// <summary>The picture is shown 1:1 - held, pinned (<see cref="ToggleZoom"/>), or following the other half.</summary>
    public bool IsZoomed => _imageZoomActive;

    /// <summary>
    /// True while the keyboard is inside the code viewer. The window asks
    /// before claiming Ctrl+F: AvalonEdit owns that key for its own search
    /// panel, and stealing it there would be surprising.
    /// </summary>
    public bool IsCodeEditorFocused => CodeEditor.IsKeyboardFocusWithin;

    /// <summary>
    /// The text on show was scrolled by the user - to these offsets,
    /// across and down. The compare window passes it to the other text
    /// (<see cref="FollowTextScroll"/>), so the two are read side by side.
    /// </summary>
    public event EventHandler<Point>? TextScrolled;

    /// <summary>
    /// The mouse came down by the picture's bar, or went away from it. Full
    /// screen, a pair's other picture brings its bar up with this one
    /// (<see cref="ReachBarWith"/>): the two are one comparison (2026-09-24).
    /// </summary>
    public event EventHandler<bool>? BarReached;

    /// <summary>
    /// The held-button zoom moved, or ended - see <see cref="ZoomMove"/>.
    /// Only <see cref="Link"/> listens: it passes the move to the other
    /// pane of a pair (<see cref="FollowZoom"/>).
    /// </summary>
    private event EventHandler<ZoomMove>? ZoomMoved;


    /// <summary>
    /// Ties the held-button zoom of two panes (PLAN Q5): the one under the
    /// mouse leads, the other shows the same place of its own picture - or
    /// stands still while the right button is held as well, which is how
    /// two frames framed apart are lined up (<see cref="ZoomLink"/>). The
    /// end of a zoom always goes across, so a half put away mid-zoom does
    /// not come back zoomed.
    /// </summary>
    /// <param name="first">One pane of the pair - the upper or the left one.</param>
    /// <param name="second">The other.</param>
    /// <param name="live">Whether a move goes across now - the split is on screen; null for always.</param>
    /// <returns>The link, for a new pair to line up afresh (<see cref="ZoomLink.Reset"/>).</returns>
    public static ZoomLink Link(PreviewPane first, PreviewPane second, Func<bool>? live = null) {
        var link = new ZoomLink();
        first.ZoomMoved += (_, move) => Relay(link, second, move, fromFirst: true, live);
        second.ZoomMoved += (_, move) => Relay(link, first, move, fromFirst: false, live);

        return link;
    }


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

    /// <summary>
    /// Lets go of the browser behind the pane on the way out, so its
    /// <c>msedgewebview2</c> processes neither outlive Wander nor keep the
    /// profile folder locked. Only when one was ever started: a web view
    /// that never initialised has nothing to stop.
    /// </summary>
    public void ReleaseWebView() {
        if (WebPreview.CoreWebView2 is not null) {
            WebPreview.Dispose();
        }
    }

    /// <summary>
    /// Puts the other half of a split inside this pane: under the content
    /// when <paramref name="stacked"/>, beside it otherwise - and above the
    /// footer either way. The footer speaks for the pair, and between the
    /// two pictures it cut them apart. Null takes the half away; the control
    /// stays in the slot, collapsed, for the next pair.
    /// </summary>
    public void ShowSecond(PreviewPane? second, bool stacked) {
        if (second is null) {
            SecondSlot.Visibility = Visibility.Collapsed;
            SecondRow.Height = new GridLength(0);
            SecondColumn.Width = new GridLength(0);

            return;
        }

        if (!ReferenceEquals(SecondSlot.Content, second)) {
            SecondSlot.Content = second;
        }
        Grid.SetRow(SecondSlot, stacked ? 1 : 0);
        Grid.SetColumn(SecondSlot, stacked ? 0 : 1);
        SecondRow.Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SecondColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        // The line between the halves is the second pane's own border, on
        // the side that faces this one.
        second.BorderThickness = stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        SecondSlot.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The room the two halves of a split share - the pane above the footer,
    /// whichever way it is split now: what decides which way it splits
    /// (SplitOrientation).
    /// </summary>
    public Size PairArea() {
        var grid = (Grid)ContentArea.Parent;
        double footer = grid.RowDefinitions[2].ActualHeight;

        return new Size(grid.ActualWidth, Math.Max(0, grid.ActualHeight - footer));
    }

    /// <summary>
    /// Ctrl+3 (PLAN B6): the keyboard into what the pane shows - the text or
    /// the code, to read, select and find in; the document; the page; the
    /// play button of a clip or a track. A pane still loading takes it when
    /// its content comes. False when nothing here takes the keyboard - a
    /// picture, a model, a folder's census: it stays where it was.
    /// </summary>
    public bool TakeKeyboard() {
        if (!IsVisible) {
            return false;
        }
        if (FocusContent()) {
            return true;
        }
        // Shown a moment ago, the file still being read - not a pane with
        // nothing selected, which would take the keyboard whenever a file
        // came to it later.
        if (Controller.Kind != PreviewKind.None || Controller.IsPlaceholderVisible) {
            return false;
        }

        _keyboardWanted = true;

        return true;
    }

    /// <summary>Scrolls the text on show to the other text's offsets - see <see cref="TextScrolled"/>.</summary>
    public void FollowTextScroll(Point offset) {
        _followingScroll = true;
        try {
            switch (Controller.Kind) {
                case PreviewKind.Text:
                    PlainText.ScrollToHorizontalOffset(offset.X);
                    PlainText.ScrollToVerticalOffset(offset.Y);
                    break;

                case PreviewKind.Code:
                    CodeEditor.ScrollToHorizontalOffset(offset.X);
                    CodeEditor.ScrollToVerticalOffset(offset.Y);
                    break;

                case PreviewKind.Document:
                    DocumentPreview.ScrollToHorizontalOffset(offset.X);
                    DocumentPreview.ScrollToVerticalOffset(offset.Y);
                    break;
            }
        } finally {
            _followingScroll = false;
        }
    }


    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi) {
        base.OnDpiChanged(oldDpi, newDpi);
        DpiScale = newDpi.DpiScaleX;
        ReportViewport();
    }


    private void ContentArea_SizeChanged(object sender, SizeChangedEventArgs e) {
        ReportViewport();
    }

    /// <summary>
    /// A scroll of the text on show - not of the find field, not of the
    /// folder census - goes out as <see cref="TextScrolled"/>. The echo of
    /// a followed scroll comes back with the same offsets and changes
    /// nothing on the other side.
    /// </summary>
    private void ContentArea_ScrollChanged(object sender, ScrollChangedEventArgs e) {
        if (_followingScroll || TextScrolled is null || !IsFindable
            || (e.HorizontalChange == 0 && e.VerticalChange == 0)
            || e.OriginalSource is not DependencyObject source || FindBar.IsAncestorOf(source)) {
            return;
        }

        TextScrolled(this, new Point(e.HorizontalOffset, e.VerticalOffset));
    }

    /// <summary>
    /// Tells the controller how many device pixels a picture has here - the
    /// content area less the picture's margin (<see cref="PictureMargin"/>) -
    /// so it decodes one to that size (PLAN AK, step 3).
    /// </summary>
    private void ReportViewport() {
        if (DataContext is not PreviewController controller || ContentArea.ActualWidth <= 0) {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var margin = PictureMargin;
        controller.SetViewport(
            (ContentArea.ActualWidth - margin.Left - margin.Right) * dpi.DpiScaleX,
            (ContentArea.ActualHeight - margin.Top - margin.Bottom) * dpi.DpiScaleY);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (e.OldValue is PreviewController old) {
            old.PropertyChanged -= OnPreviewPropertyChanged;
            old.ContentReleased -= OnContentReleased;
            old.RatingChanged -= OnRatingChanged;
        }
        if (e.NewValue is PreviewController controller) {
            controller.PropertyChanged += OnPreviewPropertyChanged;
            controller.ContentReleased += OnContentReleased;
            controller.RatingChanged += OnRatingChanged;
            UpdateCodeEditor();
            ReportViewport();
        }
    }

    /// <summary>
    /// The controller let go of its file for an operation (PLAN AF): the
    /// browser leaves the page it had open - a PDF on screen holds its file.
    /// The player is closed already: its source went with the content.
    /// </summary>
    private void OnContentReleased(object? sender, EventArgs e) {
        if (WebPreview.CoreWebView2 is { } web) {
            try { web.Navigate("about:blank"); } catch { /* the view is going away */ }
        }
    }


    [SuppressMessage("ReSharper", "AsyncVoidEventHandlerMethod",
        Justification = "A PropertyChanged handler is void by contract. It runs on the dispatcher, so an exception lands in App.HookCrashLogging (DispatcherUnhandledException): logged and offered as a report, not fatal.")]
    private async void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(PreviewController.Text):
                ForgetMatches();
                break;

            case nameof(PreviewController.CodeText):
            case nameof(PreviewController.CodeExtension):
                ForgetMatches();
                UpdateCodeEditor();
                break;

            case nameof(PreviewController.WebUri):
                if (Controller.WebUri is { } uri) {
                    await EnsureWebViewReadyAsync();
                    try { WebPreview.Source = uri; } catch { /* webview not ready */ }
                }
                break;

            case nameof(PreviewController.WebHtml):
                if (Controller.WebHtml is { } html) {
                    await EnsureWebViewReadyAsync();
                    try { WebPreview.NavigateToString(html); } catch { /* webview not ready */ }
                }
                break;

            case nameof(PreviewController.DocumentPath):
                ForgetMatches();
                await LoadDocumentAsync(Controller.DocumentPath);
                break;

            case nameof(PreviewController.Kind):
                // Bail out of any in-flight image-zoom state when the user
                // switches to a different file (e.g., the button held when changing
                // selection). Also reset the video transport so a freshly
                // opened video starts paused with the play button correct.
                ExitImageZoom();
                ResetVideoTransport();
                ResetModelView();
                if (!IsFindable) {
                    CloseFind(keepKeyboard: false);
                }
                if (_keyboardWanted) {
                    _keyboardWanted = false;
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusContent());
                }
                break;

            case nameof(PreviewController.ZoomSource):
                // Picture to picture: Kind does not change, see
                // RefreshImageZoom. Null is the pane clearing before the
                // next decode - the zoom waits for what comes. Loaded
                // priority, so the new picture has been laid out.
                if (_imageZoomActive && Controller.ZoomSource is not null) {
                    // Sized for the new picture before the next frame - the
                    // binding puts it in the old one's box, and a frame of
                    // that is a squeezed picture. Whether it still needs
                    // zooming waits for the layout.
                    UpdateZoomPosition(_zoomAt);
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefreshImageZoom);
                } else if (Controller.ZoomSource is not null && ImagePreviewHost.IsMouseOver) {
                    // A RAW's full-size JPEG arriving behind the quick one
                    // changes nothing on screen, so SizeChanged says
                    // nothing - and whether there is anything to zoom into
                    // may just have changed under the cursor.
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateImageCursor);
                }
                break;

            case nameof(PreviewController.MediaUri):
                // The controller sets Kind before MediaUri precisely so
                // that this can tell a track from a clip.
                OpenMedia(Controller.MediaUri);
                break;

            case nameof(PreviewController.ModelParts):
                ShowModel();
                break;

            case nameof(PreviewController.FindRequest):
                OnFindRequest(Controller.FindRequest);
                break;
        }
    }


    private void UpdateCodeEditor() {
        if (string.IsNullOrEmpty(Controller.CodeText)) {
            CodeEditor.Clear();
            CodeEditor.SyntaxHighlighting = null;
            return;
        }

        string ext = Controller.CodeExtension ?? "";
        // AvalonEdit ships highlighting for: C#, C++, Java, JS, TS, CSS, HTML, XML, JSON, Python, PHP, SQL, Markdown, ...
        CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        CodeEditor.Text = Controller.CodeText;
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
            bytes = await Task.Run(() => SharedRead.ReadAllBytes(path));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            DocumentPreview.Document = new FlowDocument();

            return;
        }

        // The selection may have moved on while the file was being read.
        if (!string.Equals(Controller.DocumentPath, path, StringComparison.OrdinalIgnoreCase)) {
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
        // A query handed over before the document was read (OnFindRequest),
        // or the field left open over the document before: the find that
        // ran when the load ended may have come before this read did.
        if (_pendingFind is not null || FindBar.Visibility == Visibility.Visible) {
            _pendingFind = null;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => RunFind(0));
        }
    }


    private async Task EnsureWebViewReadyAsync() {
        if (_webInitialized) {
            return;
        }
        try {
            // Explicit user-data folder: the default is "<exe dir>.WebView2",
            // which fails silently when Wander runs from a read-only location
            // (portable exe in Program Files, network share). Under the
            // system Temp when scratch copies go there (PLAN AD1).
            string dataFolder = AppPaths.WebView2;
            // The browser fetches components for itself - safe-browsing
            // lists, tracking protection, spell-check - into the profile:
            // 42 MB over two months, for a pane that shows local files with
            // the network cut off anyway (PLAN AD1). Every pane of the
            // process asks with the same options: the folder takes one set.
            var options = new CoreWebView2EnvironmentOptions {
                AdditionalBrowserArguments = "--disable-component-update --disable-background-networking",
                EnableTrackingPrevention = false,
            };
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: dataFolder, options: options);
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

    /// <summary>The keyboard onto the control that shows the content - see <see cref="TakeKeyboard"/>.</summary>
    private bool FocusContent() {
        return Controller.Kind switch {
            PreviewKind.Text => PlainText.Focus(),
            PreviewKind.Code => CodeEditor.TextArea.Focus(),
            PreviewKind.Document => DocumentPreview.Focus(),
            PreviewKind.Web => WebPreview.Focus(),
            PreviewKind.Video or PreviewKind.Audio => VideoPlayPauseButton.Focus(),
            _ => false,
        };
    }


    // --- Image zoom (FastStone-style hold-and-pan) ----------------------
    //
    // When the previewed image is downscaled to fit the pane:
    //   • the cursor turns into a magnifier glyph,
    //   • holding the left mouse button shows the image at native 1:1 with
    //     the pixel under the cursor anchored to the cursor's screen position,
    //   • moving the mouse pans the 1:1 view — release it to return.
    //
    // Geometry: as the cursor moves from (0,0) to (host.W, host.H) we map
    // linearly onto (0,0)..(src.W, src.H) image-pixel space, then position
    // the 1:1 image so that mapped pixel sits under the cursor. This
    // matches FastStone / IrfanView "navigator" zoom.

    // On the axis that does not pan the zoom view is placed as the fitted
    // picture is: PictureMargin.Top from the top. Left and right margins are
    // equal, and the centring math (hw - srcW)/2 gives the same X with or
    // without them.
    //
    // Full screen, Z pins the same zoom (ToggleZoom, 2026-09-24): no button
    // held, the mouse pans it all the same, and Z again lets it go.

    private bool _imageZoomActive;

    // The zoom stays without a button held - see ToggleZoom. The other half
    // of a pair follows pinned too, so its own mouse moves pan it.
    private bool _zoomPinned;

    // Where in the host the zoom was last put - the mouse, or the other
    // half's share of it - so a new picture under a held zoom can be put
    // at the same place without waiting for the mouse to move.
    private Point _zoomAt;

    /// <summary>
    /// What the 1:1 view draws and is measured by: the biggest picture the
    /// controller has of the file, which for a RAW is not the one fitted
    /// into the pane (<see cref="PreviewController.ZoomSource"/>).
    /// </summary>
    private BitmapSource? ZoomBitmap => (DataContext as PreviewController)?.ZoomSource as BitmapSource;


    /// <summary>
    /// The 1:1 zoom on, or off again, with no button held - Z full screen
    /// (2026-09-24). On, it stays: the mouse pans it as it pans a held one,
    /// the right button still moves this picture alone in a pair, and the
    /// next picture keeps it at the same place. It comes on where the mouse
    /// is over the picture, at the middle otherwise; a picture that fits
    /// whole has nothing to zoom into. Any zoom on here - held, pinned, or
    /// following the other half - goes off, on both halves.
    /// </summary>
    public void ToggleZoom() {
        if (_imageZoomActive) {
            ExitImageZoom();

            return;
        }
        if (!BeginImageZoom()) {
            return;
        }

        _zoomPinned = true;
        var at = ImagePreviewHost.IsMouseOver
            ? Mouse.GetPosition(ImagePreviewHost)
            : new Point(ImagePreviewHost.ActualWidth / 2, ImagePreviewHost.ActualHeight / 2);
        MoveImageZoom(at, alone: false);
    }


    private bool IsImageDownscaled() {
        if (ZoomFrame() is not { } frame) {
            return false;
        }
        // A few pixels of slop avoid jitter exactly at break-even. If the
        // frame at 1:1 (in DIPs, see BeginImageZoom) is already smaller
        // than the available render area there's nothing useful to zoom
        // into, so we don't switch the cursor.
        var dpi = VisualTreeHelper.GetDpi(this);

        return frame.Width / dpi.DpiScaleX > ImgFit.ActualWidth + 1
            || frame.Height / dpi.DpiScaleY > ImgFit.ActualHeight + 1;
    }

    /// <summary>
    /// What the 1:1 view is drawn at, in device pixels: the whole frame on
    /// show (<see cref="PreviewController.ImageCapWidth"/>, what the fitted
    /// picture is capped at), not the pixels of the picture the controller
    /// has of it so far. Between two pictures that is the copy fitted to
    /// the pane - or a RAW's quick preview, smaller than the pane - and a
    /// zoom kept across the change fell back to nearly the fitted view, or
    /// ended, until the whole frame came (2026-09-24). Meanwhile the copy is
    /// drawn stretched to the frame, and sharpens in place.
    /// </summary>
    private (double Width, double Height)? ZoomFrame() {
        if (ZoomBitmap is not { } src) {
            return null;
        }

        var controller = Controller;
        double width = double.IsFinite(controller.ImageCapWidth) ? Math.Max(controller.ImageCapWidth, src.PixelWidth) : src.PixelWidth;
        double height = double.IsFinite(controller.ImageCapHeight) ? Math.Max(controller.ImageCapHeight, src.PixelHeight) : src.PixelHeight;

        return (width, height);
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
        // A pinned zoom is on already and stays: the button adds nothing.
        if (_zoomPinned) {
            e.Handled = true;

            return;
        }
        if (!BeginImageZoom()) {
            return;
        }

        MoveImageZoom(e.GetPosition(ImagePreviewHost), alone: e.RightButton == MouseButtonState.Pressed);
        // Capture so we still get the LMB-up if the user lifts the button
        // outside the host (e.g., over the splitter). LostMouseCapture is
        // our cleanup path.
        ImagePreviewHost.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Switches to the 1:1 view; false when the picture fits whole and there is nothing to zoom into.</summary>
    private bool BeginImageZoom() {
        if (!IsImageDownscaled()) {
            return false;
        }

        // Sized and placed by UpdateZoomPosition, which every caller runs next.
        _imageZoomActive = true;
        ImgZoomCanvas.Visibility = Visibility.Visible;
        // Nothing over the pixels being looked at: the picture's bar and
        // its chart step aside until the zoom ends (ExitImageZoom).
        ReachBar(false);
        UpdateBar();

        return true;
    }

    /// <summary>
    /// Shows the other half's zoom here: the same share of this picture,
    /// without taking the mouse. Null ends it. A picture that fits whole
    /// has nothing to zoom into and stays as it is.
    /// </summary>
    /// <param name="pinned">The zoom there stays without a button - this one does too (<see cref="ToggleZoom"/>).</param>
    private void FollowZoom(Point? share, bool pinned) {
        if (share is not { } at) {
            ExitImageZoom(notify: false);

            return;
        }
        if (!_imageZoomActive && !BeginImageZoom()) {
            return;
        }

        _zoomPinned = pinned;
        UpdateZoomPosition(new Point(at.X * ImagePreviewHost.ActualWidth, at.Y * ImagePreviewHost.ActualHeight));
    }

    /// <summary>One pane's zoom, handed to the other through the link - see <see cref="Link"/>.</summary>
    private static void Relay(ZoomLink link, PreviewPane other, ZoomMove move, bool fromFirst, Func<bool>? live) {
        if (move.Share is not { } at) {
            link.End();
            other.FollowZoom(null, pinned: false);

            return;
        }
        if (live?.Invoke() == false) {
            return;
        }

        if (link.Lead(fromFirst, at.X, at.Y, move.Alone) is { } follows) {
            other.FollowZoom(new Point(follows.X, follows.Y), move.Pinned);
        }
    }

    /// <summary>
    /// A new picture under a held zoom - an arrow key while the button is
    /// down, the file rewritten on disk. <c>Kind</c> stays Image from one
    /// picture to the next, so nothing else notices, and the new one used
    /// to be drawn in the old one's box: 1:1 only if the two happened to
    /// be the same size. Now it is 1:1 at the same place, or, when it fits
    /// whole and there is nothing to zoom into, the zoom ends.
    /// </summary>
    private void RefreshImageZoom() {
        if (!_imageZoomActive) {
            return;
        }
        if (!IsImageDownscaled()) {
            // Only the half that holds the mouse speaks for the split; a
            // follower ending on its own must not end the other one.
            ExitImageZoom(notify: ImagePreviewHost.IsMouseCaptured);

            return;
        }

        UpdateZoomPosition(_zoomAt);
    }

    /// <summary>The zoom follows the mouse here, and the other half of a split is told where.</summary>
    /// <param name="mouse">Where the mouse is, in the picture area.</param>
    /// <param name="alone">The right button is held as well: the other half stays where it is.</param>
    private void MoveImageZoom(Point mouse, bool alone) {
        UpdateZoomPosition(mouse);

        double hw = ImagePreviewHost.ActualWidth;
        double hh = ImagePreviewHost.ActualHeight;
        if (hw > 0 && hh > 0) {
            ZoomMoved?.Invoke(this, new ZoomMove(
                new Point(Math.Clamp(mouse.X / hw, 0, 1), Math.Clamp(mouse.Y / hh, 0, 1)), alone, _zoomPinned));
        }
    }

    private void ImageZoom_LmbUp(object sender, MouseButtonEventArgs e) {
        if (!_zoomPinned) {
            ExitImageZoom();
        }
        e.Handled = true;
    }

    private void ImageZoom_MouseMove(object sender, MouseEventArgs e) {
        if (!_imageZoomActive) {
            return;
        }
        // Defensive: if LMB was released while we missed an event (e.g.,
        // capture got stolen), drop out of zoom - unless no button holds it.
        if (!_zoomPinned && e.LeftButton != MouseButtonState.Pressed) {
            ExitImageZoom();
            return;
        }
        MoveImageZoom(e.GetPosition(ImagePreviewHost), alone: e.RightButton == MouseButtonState.Pressed);
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
        _zoomAt = mouse;
        if (ZoomBitmap is not { } src || ZoomFrame() is not { } frame) {
            return;
        }
        double hw = ImagePreviewHost.ActualWidth;
        double hh = ImagePreviewHost.ActualHeight;
        if (hw <= 0 || hh <= 0) {
            return;
        }

        // 1 frame pixel = 1 device pixel, FastStone's "100 %": the size in
        // DIPs is the pixel size over the monitor's scale. Read here, not
        // kept in a field - the window may have moved to another monitor.
        // Sized on every move rather than once, so the box always belongs
        // to the picture now in ImgFit, not the one the zoom began on.
        var dpi = VisualTreeHelper.GetDpi(this);
        double srcW = frame.Width / dpi.DpiScaleX;
        double srcH = frame.Height / dpi.DpiScaleY;
        ImgZoom.Width = srcW;
        ImgZoom.Height = srcH;
        // Pixel for pixel once the whole frame is here; a copy smaller than
        // it, stretched until then (ZoomFrame), smoothly rather than in blocks.
        RenderOptions.SetBitmapScalingMode(ImgZoom, src.PixelWidth < frame.Width || src.PixelHeight < frame.Height
            ? BitmapScalingMode.Linear
            : BitmapScalingMode.NearestNeighbor);
        // The helpers' marks are a picture of the same pixels; they are
        // sized and placed with it rather than bound, so the two can never
        // be a frame apart.
        ImgZoomOverlay.Width = srcW;
        ImgZoomOverlay.Height = srcH;

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
        // uses - ImgFit is VerticalAlignment="Top" with PictureMargin, so
        // the fit view places the image at y = PictureMargin.Top. Centring
        // vertically here would visibly jump the image down when the user
        // holds LMB.
        double y = srcH > hh
            ? my - (my / hh) * srcH
            : PictureMargin.Top;

        // On whole device pixels: a fractional offset resamples the whole
        // picture by a share of a pixel, and 1:1 stops being 1:1.
        double left = Math.Round(x * dpi.DpiScaleX) / dpi.DpiScaleX;
        double top = Math.Round(y * dpi.DpiScaleY) / dpi.DpiScaleY;
        Canvas.SetLeft(ImgZoom, left);
        Canvas.SetTop(ImgZoom, top);
        Canvas.SetLeft(ImgZoomOverlay, left);
        Canvas.SetTop(ImgZoomOverlay, top);
    }

    /// <param name="notify">
    /// Tell the other half of a split. Not when this pane was only following
    /// it: the end came from there.
    /// </param>
    private void ExitImageZoom(bool notify = true) {
        if (!_imageZoomActive) {
            return;
        }
        _imageZoomActive = false;
        _zoomPinned = false;
        ImgZoomCanvas.Visibility = Visibility.Collapsed;
        UpdateBar();
        if (ImagePreviewHost.IsMouseCaptured) {
            ImagePreviewHost.ReleaseMouseCapture();
        }
        UpdateImageCursor();
        if (notify) {
            ZoomMoved?.Invoke(this, new ZoomMove(null, false, false));
        }
    }


    // --- The picture's bar (PLAN Q5) -------------------------------------
    //
    // Full screen and in each half of a split a picture carries its own
    // stars and sharpness score (and, full screen, the helpers' switches)
    // in its lower left corner. They are there for a moment and the picture
    // for the rest, so they stand at BarAtRest of their strength - 30% in
    // the pane, nothing full screen - until the mouse comes down to them or
    // the rating changes - a digit pressed, a star clicked, an undo - and
    // then for a moment more. The chart of levels in the other corner
    // (PictureChart) comes and goes with them where they go away whole.

    /// <summary>How near the bottom edge the mouse brings the bar up, in layout units.</summary>
    private const double BarReach = 96;

    /// <summary>How long a changed rating holds the bar up.</summary>
    private const int BarFlashMs = 1500;

    private const int BarFadeMs = 60;

    // The mouse is down by the bar - here, or by the other picture's of a
    // pair full screen (ReachBarWith); the bar is up at full strength.
    private bool _barReached;
    private bool _barReachedThere;
    private bool _barUp;

    // Holds the bar up for a moment after the rating changed.
    private DispatcherTimer? _barFlash;


    /// <summary>
    /// The chart's strength at rest: whole where the bar stays as a trace -
    /// the chart was switched on to be read - and none where the bar goes
    /// away entirely: full screen nothing is over the picture until called for.
    /// </summary>
    private double ChartAtRest => BarAtRest > 0 ? 1 : 0;


    /// <summary>
    /// Brings the picture's bar up for a moment: a rating key pressed full
    /// screen shows what it set, whether or not that changed anything.
    /// </summary>
    public void FlashBar() {
        if (_barFlash is null) {
            _barFlash = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BarFlashMs) };
            _barFlash.Tick += (_, _) => {
                _barFlash.Stop();
                UpdateBar();
            };
        }

        _barFlash.Stop();
        _barFlash.Start();
        UpdateBar();
    }

    /// <summary>
    /// The mouse is down by the other picture's bar, or has gone from it:
    /// this bar comes up and goes with that one - see <see cref="BarReached"/>.
    /// </summary>
    public void ReachBarWith(bool reached) {
        if (_barReachedThere == reached) {
            return;
        }

        _barReachedThere = reached;
        UpdateBar();
    }


    private void ContentArea_MouseMove(object sender, MouseEventArgs e) {
        ReachBar(!_imageZoomActive && e.GetPosition(ContentArea).Y >= ContentArea.ActualHeight - BarReach);
    }

    private void ContentArea_MouseLeave(object sender, MouseEventArgs e) {
        ReachBar(false);
    }

    private void ReachBar(bool reached) {
        if (_barReached == reached) {
            return;
        }

        _barReached = reached;
        UpdateBar();
        BarReached?.Invoke(this, reached);
    }

    private void OnRatingChanged(object? sender, EventArgs e) {
        FlashBar();
    }

    /// <summary>
    /// Full strength while the mouse is down by the bar or a new rating is
    /// being shown; <see cref="BarAtRest"/> and <see cref="ChartAtRest"/>
    /// otherwise. Neither over a zoom - but for a rating key's moment, which
    /// is there to show what the key set.
    /// </summary>
    private void UpdateBar() {
        bool flashing = _barFlash?.IsEnabled == true;
        double over = _imageZoomActive && !flashing ? 0 : 1;
        PictureBar.Opacity = over;
        PictureChart.Opacity = over;

        bool up = _barReached || _barReachedThere || flashing;
        if (up == _barUp) {
            return;
        }

        _barUp = up;
        var fade = TimeSpan.FromMilliseconds(BarFadeMs);
        PictureControls.BeginAnimation(OpacityProperty, new DoubleAnimation(up ? 1 : BarAtRest, fade));
        PictureChartFace.BeginAnimation(OpacityProperty, new DoubleAnimation(up ? 1 : ChartAtRest, fade));
    }

    /// <summary>The strength at rest changed: a bar at rest takes it at once.</summary>
    private void RestBar() {
        if (_barUp) {
            return;
        }

        PictureControls.BeginAnimation(OpacityProperty, null);
        PictureControls.Opacity = BarAtRest;
        PictureChartFace.BeginAnimation(OpacityProperty, null);
        PictureChartFace.Opacity = ChartAtRest;
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
        _transportIsAudio = Controller.Kind == PreviewKind.Audio;

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
        foreach (var part in Controller.ModelParts) {
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
        if (!Controller.HasModel) {
            return;
        }

        var centre = Controller.ModelCenter;
        double radius = Controller.ModelRadius;
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
        var centre = Controller.ModelCenter;
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
        if (!Controller.HasModel) {
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
        if (!Controller.HasModel) {
            return;
        }

        _modelZoom = Math.Clamp(_modelZoom * (e.Delta > 0 ? 0.85 : 1.0 / 0.85), ModelMinZoom, ModelMaxZoom);
        PlaceModelCamera();
        e.Handled = true;
    }


    // --- Find in the text (PLAN B6) ---------------------------------------

    // Where the query occurs: offsets into the plain text or the code, or
    // ranges of the rich-text document - its text is spread over runs, and
    // an offset into the whole means nothing to a TextPointer. The one on
    // show is _findAt; -1 is none.
    private IReadOnlyList<int> _findOffsets = Array.Empty<int>();
    private readonly List<TextRange> _findRanges = new();
    private int _findAt = -1;

    // Set while the field is filled from code: its TextChanged is not the user typing.
    private bool _fillingFind;

    // A query handed over before the rich-text document was read (LoadDocumentAsync).
    private string? _pendingFind;

    // The matches past the part of the text on show (PLAN B6): how many,
    // and the count still running for the query in the field.
    private int _beyond;
    private CancellationTokenSource? _beyondCts;

    private bool IsFindable => Controller.Kind is PreviewKind.Text or PreviewKind.Code or PreviewKind.Document;

    /// <summary>The keyboard is in this pane's find field - Esc there closes the field, not the pane (MainWindow).</summary>
    public bool IsFindFocused => FindBox.IsKeyboardFocusWithin;

    private int FindCountOf => Controller.Kind == PreviewKind.Document ? _findRanges.Count : _findOffsets.Count;

    /// <summary>
    /// Opens the find field over the text, with the keyboard in it -
    /// Ctrl+F when the keyboard is in this pane and the pane shows text.
    /// Starts from what is selected in the text, when that is one line.
    /// False when this pane has nothing to find in; the window's own Ctrl+F
    /// then stands.
    /// </summary>
    public bool OpenFind() {
        // The other half of a split sits inside this pane (SecondSlot): the
        // keyboard in it is that pane's to answer for.
        if (!IsKeyboardFocusWithin || SecondSlot.IsKeyboardFocusWithin || !IsFindable) {
            return false;
        }

        if (SelectedLine() is { } selected) {
            FillFind(selected);
        }
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
        RunFind(CaretOffset());

        return true;
    }

    /// <summary>
    /// F3 / Shift+F3 (PLAN B6): the next or the previous match of the find
    /// field open over the text. Past the last match with
    /// <paramref name="canLeave"/> nothing moves and the answer says so -
    /// the window goes on to the next file a search inside files found;
    /// otherwise round to the first, as Enter in the field does.
    /// </summary>
    public FindStep FindAgain(bool backwards, bool canLeave) {
        if (!IsVisible || FindBar.Visibility != Visibility.Visible || !IsFindable || FindBox.Text.Length == 0) {
            return FindStep.None;
        }

        int count = FindCountOf;
        if (canLeave && !backwards && (count == 0 || _findAt == count - 1)) {
            return FindStep.PastLast;
        }

        StepFind(backwards);

        return FindStep.Stepped;
    }


    /// <summary>
    /// The text just shown came from a search inside files: the field opens
    /// with that search's query on its first match, and the keyboard stays
    /// where it is - the user is walking the results with the arrow keys.
    /// Null closes nothing: a field the user opened stays, and finds again
    /// in the new text.
    /// </summary>
    private void OnFindRequest(string? query) {
        if (query is null) {
            if (FindBar.IsVisible) {
                AfterTextChanged();
            }

            return;
        }

        FillFind(query);
        FindBar.Visibility = Visibility.Visible;
        if (Controller.Kind == PreviewKind.Document && _findRanges.Count == 0 && DocumentPreview.Document.Blocks.Count == 0) {
            _pendingFind = query;

            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => RunFind(0));
    }

    /// <summary>The text changed under an open field: the same query, found again from the top.</summary>
    private void AfterTextChanged() {
        if (!IsFindable) {
            CloseFind(keepKeyboard: false);

            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => RunFind(0));
    }

    private void FillFind(string text) {
        _fillingFind = true;
        FindBox.Text = text;
        _fillingFind = false;
    }

    private void RunFind(int from) {
        if (FindBar.Visibility != Visibility.Visible || !IsFindable) {
            return;
        }

        string query = FindBox.Text;
        _findRanges.Clear();
        _findOffsets = Array.Empty<int>();
        switch (Controller.Kind) {
            case PreviewKind.Text:
                _findOffsets = TextFind.All(ShownPart(PlainText.Text), query);
                _findAt = TextFind.FirstFrom(_findOffsets, from);
                break;

            case PreviewKind.Code:
                _findOffsets = TextFind.All(ShownPart(CodeEditor.Text), query);
                _findAt = TextFind.FirstFrom(_findOffsets, from);
                break;

            case PreviewKind.Document:
                FindInDocument(query);
                _findAt = _findRanges.Count > 0 ? 0 : -1;
                break;
        }
        ShowMatch();
        CountBeyond(query);
    }

    /// <summary>The text on show without the note about the rest of the file - its words are no match.</summary>
    private string ShownPart(string text) {
        return Controller.ShownTextLength is { } length && length < text.Length ? text[..length] : text;
    }

    /// <summary>
    /// Counts the matches past the part on show, when the text goes on past
    /// it (PLAN B6, 2026-09-25): off the UI thread, dropped when the query
    /// or the text changes first.
    /// </summary>
    private void CountBeyond(string query) {
        _beyondCts?.Cancel();
        _beyondCts = null;
        _beyond = 0;
        ShowBeyond();
        if (query.Length == 0 || !Controller.TextGoesOn) {
            return;
        }

        _beyondCts = new CancellationTokenSource();
        _ = CountBeyondAsync(query, _beyondCts.Token);
    }

    private async Task CountBeyondAsync(string query, CancellationToken ct) {
        int count;
        try {
            count = await Controller.CountPastShownAsync(query, ct);
        } catch (OperationCanceledException) {
            return;
        }
        if (ct.IsCancellationRequested) {
            return;
        }

        _beyond = count;
        ShowBeyond();
    }

    private void ShowBeyond() {
        FindBeyond.Text = _beyond <= 0 ? ""
            : string.Format(Strings.PreviewFindBeyond, _beyond >= TextFind.MaxMatches ? $"{_beyond}+" : _beyond.ToString());
        FindBeyond.Visibility = _beyond > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Every occurrence in the rich-text document, run by run. One that
    /// crosses a change of formatting is not found - the price of not
    /// flattening the document into text and back.
    /// </summary>
    private void FindInDocument(string query) {
        if (query.Length == 0) {
            return;
        }

        var pointer = DocumentPreview.Document.ContentStart;
        while (pointer is not null && _findRanges.Count < TextFind.MaxMatches) {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text) {
                string run = pointer.GetTextInRun(LogicalDirection.Forward);
                foreach (int hit in TextFind.All(run, query)) {
                    var start = pointer.GetPositionAtOffset(hit);
                    var end = start?.GetPositionAtOffset(query.Length);
                    if (start is not null && end is not null) {
                        _findRanges.Add(new TextRange(start, end));
                    }
                }
            }
            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }
    }

    private void StepFind(bool backwards) {
        _findAt = TextFind.Step(_findAt, FindCountOf, backwards);
        ShowMatch();
    }

    /// <summary>Selects the match on show, scrolls it into view, and says which of how many it is.</summary>
    private void ShowMatch() {
        int count = FindCountOf;
        string total = count >= TextFind.MaxMatches ? $"{count}+" : count.ToString();
        FindCount.Text = count > 0 ? string.Format(Strings.PreviewFindCount, _findAt + 1, total)
            : FindBox.Text.Length > 0 ? Strings.PreviewFindNone
            : "";
        if (_findAt < 0 || _findAt >= count) {
            return;
        }

        int length = FindBox.Text.Length;
        switch (Controller.Kind) {
            case PreviewKind.Text:
                int start = _findOffsets[_findAt];
                PlainText.Select(start, length);
                PlainText.ScrollToLine(PlainText.GetLineIndexFromCharacterIndex(start));
                break;

            case PreviewKind.Code:
                int at = _findOffsets[_findAt];
                CodeEditor.Select(at, length);
                var location = CodeEditor.Document.GetLocation(at);
                CodeEditor.ScrollTo(location.Line, location.Column);
                break;

            case PreviewKind.Document:
                var range = _findRanges[_findAt];
                DocumentPreview.Selection.Select(range.Start, range.End);
                var rect = range.Start.GetCharacterRect(LogicalDirection.Forward);
                if (rect.Top < 0 || rect.Bottom > DocumentPreview.ViewportHeight) {
                    DocumentPreview.ScrollToVerticalOffset(DocumentPreview.VerticalOffset + rect.Top - (DocumentPreview.ViewportHeight / 3));
                }
                break;
        }
    }

    /// <param name="keepKeyboard">Esc in the field: the keyboard goes back to the text, not to the list.</param>
    private void CloseFind(bool keepKeyboard) {
        FindBar.Visibility = Visibility.Collapsed;
        ForgetMatches();
        _pendingFind = null;
        if (!keepKeyboard) {
            return;
        }

        switch (Controller.Kind) {
            case PreviewKind.Text:
                PlainText.Focus();
                break;

            case PreviewKind.Code:
                CodeEditor.TextArea.Focus();
                break;

            case PreviewKind.Document:
                DocumentPreview.Focus();
                break;
        }
    }

    /// <summary>
    /// The text under the field went - a new file is loading, or the same
    /// one again (a log the watcher re-read): its matches point into text
    /// that is not there any more, and a step to one of them selected past
    /// the end and threw. The field stays; the end of the load finds again
    /// (AfterTextChanged, LoadDocumentAsync).
    /// </summary>
    private void ForgetMatches() {
        _findOffsets = Array.Empty<int>();
        _findRanges.Clear();
        _findAt = -1;
        _beyondCts?.Cancel();
        _beyondCts = null;
        _beyond = 0;
        ShowBeyond();
    }

    /// <summary>Where a search from the caret starts: the caret of the text on show.</summary>
    private int CaretOffset() {
        return Controller.Kind switch {
            PreviewKind.Text => PlainText.SelectionStart,
            PreviewKind.Code => CodeEditor.SelectionStart,
            _ => 0,
        };
    }

    /// <summary>The selected text of the text on show, when it is one short line - what Ctrl+F starts with.</summary>
    private string? SelectedLine() {
        string selected = Controller.Kind switch {
            PreviewKind.Text => PlainText.SelectedText,
            PreviewKind.Code => CodeEditor.SelectedText,
            PreviewKind.Document => DocumentPreview.Selection.Text,
            _ => "",
        };

        return selected.Length is > 0 and <= 200 && !selected.Contains('\n') ? selected : null;
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e) {
        if (_fillingFind) {
            return;
        }

        // Typing on: from the match on show, so a longer query stays where it was.
        int from = _findAt >= 0 && _findAt < _findOffsets.Count ? _findOffsets[_findAt] : CaretOffset();
        RunFind(from);
    }

    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.Enter:
                StepFind(backwards: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                break;

            case Key.Escape:
                CloseFind(keepKeyboard: true);
                e.Handled = true;
                break;
        }
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e) {
        StepFind(backwards: true);
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) {
        StepFind(backwards: false);
    }

    private void FindClose_Click(object sender, RoutedEventArgs e) {
        CloseFind(keepKeyboard: FindBox.IsKeyboardFocusWithin);
    }


    /// <summary>
    /// Where the held-button zoom went: a place of the picture area as
    /// shares of it (0..1 each way), or null when it ended.
    /// </summary>
    /// <param name="Alone">The right button is held as well: the other pane of a pair stays where it is.</param>
    /// <param name="Pinned">No button holds it - Z did (<see cref="ToggleZoom"/>): the other pane's follows pinned too.</param>
    private readonly record struct ZoomMove(Point? Share, bool Alone, bool Pinned);


    /// <summary>What F3 did here - see <see cref="FindAgain"/>.</summary>
    public enum FindStep {
        /// <summary>No find field open over a text: the key is not this pane's.</summary>
        None,

        /// <summary>Moved to the next or the previous match.</summary>
        Stepped,

        /// <summary>The last match was on show already, or there is none in the text on show.</summary>
        PastLast,
    }
}
