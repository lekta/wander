using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using ImageMetadata = Wander.Core.Icons.ImageMetadata;

namespace Wander.App.ViewModels;

/// <summary>
/// Owns everything that renders inside the preview pane: the content-kind
/// switch (image / text / code / web), the async load pipeline, and the
/// footer summary (single file, folder, multi-select, current folder).
///
/// <para>
/// The hosting <see cref="MainViewModel"/> still owns layout state
/// (<c>IsPreviewVisible</c>, <c>PreviewWidth</c>) — those are persisted in
/// <see cref="Wander.Core.Persistence.AppState"/> and bound by XAML to the
/// splitter, not to content. MainVM feeds the controller via the small
/// <c>Set*</c> methods, and the controller decides whether to re-run the
/// content load, the summary, or both.
/// </para>
/// </summary>
public sealed class PreviewController : ObservableObject {
    /// <summary>
    /// RAW containers: routed through <see cref="RawPreviewExtractor"/>
    /// first, because handing these to WIC means decoding sensor data —
    /// about a hundred times slower than the JPEG the file already carries.
    /// Formats whose container we can't read still fall through to WIC, so
    /// listing one here is never worse than not listing it.
    /// </summary>
    private static readonly HashSet<string> _rawExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
    };

    private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".png", ".jpg", ".jpeg", ".bmp", ".ico", ".tif", ".tiff",
        // RAW (may not render but we still try; metadata works regardless):
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
    };

    // Animated formats go through GifImage, which composites multi-frame
    // streams. WEBP files are usually static, but WIC's WebP codec can
    // surface multiple frames for animated WEBPs; GifImage handles the
    // single-frame case by just showing that one frame, so routing all
    // .webp here costs nothing for static files and unlocks playback for
    // animated ones.
    private static readonly HashSet<string> _gifExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".gif", ".webp",
    };

    // MediaElement uses Windows Media Foundation, which on Win10/11 supports
    // these out of the box. MKV / WEBM are listed but may fall back to
    // "Unsupported" if the user hasn't installed extension packs.
    private static readonly HashSet<string> _videoExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm",
    };

    private static readonly HashSet<string> _textExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".txt", ".log", ".csv", ".tsv",
        ".ini", ".cfg", ".conf", ".toml", ".env", ".gitignore", ".gitattributes",
        ".editorconfig",
    };

    private static readonly HashSet<string> _codeExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".cs", ".csproj", ".props", ".targets", ".sln", ".slnx",
        ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs",
        ".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift", ".php",
        ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".m", ".mm",
        ".css", ".scss", ".less",
        ".sh", ".ps1", ".bat", ".cmd",
        ".sql",
        ".xml", ".xaml", ".svg",
        ".json", ".yaml", ".yml",
    };

    private const long PreviewMaxFileSize = 1_048_576;     // 1 MB
    private const int PreviewMaxChars = 200_000;


    private readonly IImageMetadataReader? _metadataReader;
    private readonly CompanionMetadataService? _companionMetadata;

    private bool _isVisible;
    private FileSystemEntry? _primary;
    private IReadOnlyList<FileSystemEntry> _selection = Array.Empty<FileSystemEntry>();
    private string? _currentFolderPath;
    private string _currentFolderName = "";

    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _summaryCts;

    private PreviewKind _kind = PreviewKind.None;
    private bool _isLoading;
    private string? _text;
    private ImageSource? _image;
    private string? _codeText;
    private string? _codeExtension;
    private Uri? _webUri;
    private string? _webHtml;
    private Uri? _gifUri;
    private Uri? _videoUri;
    private ImageMetadata? _imageMetadata;
    private string _summary = "";

    private CancellationTokenSource? _companionCts;
    private string _companionFiles = "";
    private string? _unityGuid;
    private string? _unityDetail;
    private string? _ratingPath;
    private int _rank;
    private int _colorLabel;
    private string _customColorLabel = "";


    public PreviewController(IImageMetadataReader? metadataReader, CompanionMetadataService? companionMetadata) {
        _metadataReader = metadataReader;
        _companionMetadata = companionMetadata;

        // Adobe's colour-label palette, in the index order both formats use.
        ColorLabelChoices = new[] {
            new ColorLabelViewModel(1, new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F))),
            new ColorLabelViewModel(2, new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x2C))),
            new ColorLabelViewModel(3, new SolidColorBrush(Color.FromRgb(0x5C, 0xA9, 0x4D))),
            new ColorLabelViewModel(4, new SolidColorBrush(Color.FromRgb(0x3E, 0x7C, 0xC4))),
            new ColorLabelViewModel(5, new SolidColorBrush(Color.FromRgb(0x8A, 0x5C, 0xB8))),
        };

        SetRankCommand = new RelayCommand(p => SetRating(RatingField.Rank, p, _rank), _ => HasRating);
        SetColorLabelCommand = new RelayCommand(p => SetRating(RatingField.ColorLabel, p, _colorLabel), _ => HasRating);
        CopyGuidCommand = new RelayCommand(_ => CopyGuid(), _ => HasUnityGuid);
    }


    // --- Output properties (XAML binds Preview.X) ----------------------

    public PreviewKind Kind {
        get => _kind;
        private set {
            if (SetField(ref _kind, value)) {
                Raise(nameof(IsPlaceholderVisible));
                Raise(nameof(PlaceholderText));
            }
        }
    }

    public bool IsLoading {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string? Text {
        get => _text;
        private set => SetField(ref _text, value);
    }

    public ImageSource? Image {
        get => _image;
        private set => SetField(ref _image, value);
    }

    public string? CodeText {
        get => _codeText;
        private set => SetField(ref _codeText, value);
    }

    public string? CodeExtension {
        get => _codeExtension;
        private set => SetField(ref _codeExtension, value);
    }

    public Uri? WebUri {
        get => _webUri;
        private set => SetField(ref _webUri, value);
    }

    public string? WebHtml {
        get => _webHtml;
        private set => SetField(ref _webHtml, value);
    }

    public Uri? GifUri {
        get => _gifUri;
        private set => SetField(ref _gifUri, value);
    }

    public Uri? VideoUri {
        get => _videoUri;
        private set => SetField(ref _videoUri, value);
    }

    public ImageMetadata? ImageMetadata {
        get => _imageMetadata;
        private set => SetField(ref _imageMetadata, value);
    }

    public string Summary {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    // --- Companion ("integrated item") block ---------------------------
    // Everything below describes the sidecars folded into the selected
    // row. It sits under the summary in the preview footer, which is where
    // the answer to "what is this file's GUID / how did I rate this shot"
    // belongs: next to the file, not behind a dialog.

    /// <summary>Names of the companion files, comma-separated. Empty when there are none.</summary>
    public string CompanionFiles {
        get => _companionFiles;
        private set {
            if (SetField(ref _companionFiles, value)) {
                Raise(nameof(HasCompanions));
            }
        }
    }

    public bool HasCompanions => _companionFiles.Length > 0;

    /// <summary>Unity asset GUID, or null when the selection has no <c>.meta</c>.</summary>
    public string? UnityGuid {
        get => _unityGuid;
        private set {
            if (SetField(ref _unityGuid, value)) {
                Raise(nameof(HasUnityGuid));
            }
        }
    }

    public bool HasUnityGuid => !string.IsNullOrEmpty(_unityGuid);

    /// <summary>Importer name / "folder asset" — context for the GUID above.</summary>
    public string? UnityDetail {
        get => _unityDetail;
        private set => SetField(ref _unityDetail, value);
    }

    /// <summary>
    /// Whether the selection carries a sidecar whose rating can be shown and
    /// edited — a RawTherapee <c>.pp3</c> or an XMP.
    /// </summary>
    public bool HasRating => _ratingPath is not null;

    /// <summary>Name of the file the rating is read from and written to, for the tooltip.</summary>
    public string RatingSource => _ratingPath is null ? "" : Path.GetFileName(_ratingPath);

    /// <summary>Stars currently written in the sidecar, 0…5.</summary>
    public int Rank {
        get => _rank;
        private set => SetField(ref _rank, value);
    }

    /// <summary>
    /// The five colour swatches, always present so the row doesn't change
    /// shape as the selection moves. Which one reads as chosen is the
    /// swatch's own <c>IsSelected</c>.
    /// </summary>
    public IReadOnlyList<ColorLabelViewModel> ColorLabelChoices { get; }

    /// <summary>Free-text colour label an XMP carried that isn't one of the standard five.</summary>
    public string CustomColorLabel {
        get => _customColorLabel;
        private set => SetField(ref _customColorLabel, value);
    }

    /// <summary>Writes a new star count into the sidecar. Parameter is the star clicked, 1…5.</summary>
    public RelayCommand SetRankCommand { get; }

    /// <summary>Writes a colour label into the sidecar. Parameter is the swatch index, 1…5.</summary>
    public RelayCommand SetColorLabelCommand { get; }

    /// <summary>Puts the Unity GUID on the clipboard — the reason it's shown at all.</summary>
    public RelayCommand CopyGuidCommand { get; }

    /// <summary>Status line for the last companion write, shown next to the stars.</summary>
    public string CompanionStatus {
        get => _companionStatus;
        private set => SetField(ref _companionStatus, value);
    }

    private string _companionStatus = "";


    public bool IsPlaceholderVisible =>
        _isVisible && (_kind == PreviewKind.None || _kind == PreviewKind.Unsupported);

    public string PlaceholderText =>
        _kind == PreviewKind.None ? Strings.PreviewSelectFile : Strings.PreviewUnsupported;


    // --- Folder census (B2) --------------------------------------------
    // What the content area shows when the selection is a folder, or when
    // nothing is selected and the current folder is the subject. A grid of
    // thumbnails was the alternative; the census answers "what is in here
    // and what is eating the space", which is the question a folder
    // actually raises.

    /// <summary>Headline over the type table: file / folder counts and total size.</summary>
    public string FolderHeadline {
        get => _folderHeadline;
        private set => SetField(ref _folderHeadline, value);
    }

    /// <summary>Name of the folder being described.</summary>
    public string FolderTitle {
        get => _folderTitle;
        private set => SetField(ref _folderTitle, value);
    }

    /// <summary>Non-empty when the walk hit its budget and the numbers are a floor.</summary>
    public string FolderNote {
        get => _folderNote;
        private set {
            if (SetField(ref _folderNote, value)) {
                Raise(nameof(HasFolderNote));
            }
        }
    }

    public bool HasFolderNote => _folderNote.Length > 0;

    /// <summary>Biggest file types first, with a bar proportional to their share.</summary>
    public ObservableCollection<FolderTypeRow> FolderTypes { get; } = new();

    private string _folderHeadline = "";
    private string _folderTitle = "";
    private string _folderNote = "";


    // --- Inputs from MainViewModel -------------------------------------

    public void SetVisible(bool visible) {
        if (_isVisible == visible) {
            return;
        }
        _isVisible = visible;
        Raise(nameof(IsPlaceholderVisible));
        SchedulePreviewUpdate();
        ScheduleSummaryUpdate();
        ScheduleCompanionUpdate();
    }

    public void SetPrimary(FileSystemEntry? entry) {
        if (ReferenceEquals(_primary, entry)) {
            return;
        }
        _primary = entry;
        SchedulePreviewUpdate();
        ScheduleSummaryUpdate();
        ScheduleCompanionUpdate();
    }


    /// <summary>
    /// Re-reads the sidecars of the current selection. Needed after a
    /// Ctrl+Z that put an old rating back: the file changed underneath a
    /// footer that is otherwise only refreshed by a new selection.
    /// </summary>
    public void ReloadCompanions() {
        ScheduleCompanionUpdate();
    }

    public void SetSelection(IReadOnlyList<FileSystemEntry> selection) {
        _selection = selection ?? Array.Empty<FileSystemEntry>();
        ScheduleSummaryUpdate();
    }

    public void SetCurrentFolder(string? path, string name) {
        if (_currentFolderPath == path && _currentFolderName == name) {
            return;
        }
        _currentFolderPath = path;
        _currentFolderName = name;
        ScheduleSummaryUpdate();
    }


    // --- Preview content pipeline --------------------------------------

    private void SchedulePreviewUpdate() {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        _ = UpdatePreviewAsync(_previewCts.Token);
    }

    private async Task UpdatePreviewAsync(CancellationToken ct) {
        ClearPreviewContent();

        if (!_isVisible) {
            Kind = PreviewKind.None;
            IsLoading = false;
            return;
        }

        // A folder — or an empty selection with a folder open — gets the
        // census instead of "Select a file to preview". Recycled folders do
        // not: their backing path under $Recycle.Bin is not reliably
        // walkable, and the footer already says where they came from.
        if (_primary is null || _primary.Kind != EntryKind.File) {
            string? folder = _primary?.Kind == EntryKind.Directory && _primary.OriginalLocation is null
                ? _primary.FullPath
                : _primary is null ? _currentFolderPath : null;
            if (folder is not null) {
                await ShowFolderCensusAsync(folder, ct);
                return;
            }

            Kind = PreviewKind.None;
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try {
            string path = _primary.FullPath;
            string ext = Path.GetExtension(path);

            if (_gifExtensions.Contains(ext)) {
                LoadGif(path);
                return;
            }

            if (_videoExtensions.Contains(ext)) {
                LoadVideo(path);
                return;
            }

            if (_imageExtensions.Contains(ext)) {
                await LoadImageAsync(path, ct);
                return;
            }

            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".htm", StringComparison.OrdinalIgnoreCase)) {
                WebUri = new Uri(path);
                Kind = PreviewKind.Web;
                return;
            }

            if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)) {
                await LoadMarkdownAsync(path, ct);
                return;
            }

            if (_codeExtensions.Contains(ext)) {
                await LoadCodeAsync(path, ext, ct);
                return;
            }

            if (_textExtensions.Contains(ext) || string.IsNullOrEmpty(ext)) {
                await LoadTextAsync(path, ct);
                return;
            }

            Kind = PreviewKind.Unsupported;
        } catch (OperationCanceledException) {
            // newer selection won — ignore
        } finally {
            if (!ct.IsCancellationRequested) {
                IsLoading = false;
                ScheduleSummaryUpdate();  // metadata might have arrived
            }
        }
    }

    private void LoadGif(string path) {
        // GifImage decodes the file lazily on the UI thread; we just set the URI.
        // Metadata (pixel size, EXIF if any) goes through the same reader as
        // for normal images so the footer summary line still works.
        GifUri = new Uri(path);
        if (_metadataReader is not null) {
            try { ImageMetadata = _metadataReader.Read(path); } catch { /* best effort */ }
        }
        Kind = PreviewKind.Gif;
    }

    private void LoadVideo(string path) {
        // MediaElement does its own threaded decode; we just hand it the URI.
        // No metadata extraction (MetadataExtractor's container support varies
        // by format; not worth the bytes here for v1).
        VideoUri = new Uri(path);
        Kind = PreviewKind.Video;
    }

    private async Task LoadImageAsync(string path, CancellationToken ct) {
        BitmapImage? image = null;
        ImageMetadata? meta = null;

        await Task.Run(() => {
            ct.ThrowIfCancellationRequested();
            image = _rawExtensions.Contains(Path.GetExtension(path)) ? LoadRawPreview(path) : null;
            image ??= Decode(bi => bi.UriSource = new Uri(path));

            if (_metadataReader is not null) {
                meta = _metadataReader.Read(path);
            }
        }, ct);

        if (ct.IsCancellationRequested) {
            return;
        }

        ImageMetadata = meta;
        if (image is not null) {
            Image = image;
            Kind = PreviewKind.Image;
        } else {
            Kind = PreviewKind.Unsupported;
        }
    }

    /// <summary>
    /// RAW files get their embedded JPEG preview rather than a full sensor
    /// decode — measured at ~10 ms against ~1200 ms for a 33 MB CR3. Null
    /// when the file has no usable preview; the caller then falls back to
    /// the ordinary decode, so an unrecognised container costs nothing but
    /// the old behaviour.
    /// </summary>
    private static BitmapImage? LoadRawPreview(string path) {
        byte[]? jpeg;
        try {
            using var file = File.OpenRead(path);
            jpeg = RawPreviewExtractor.Extract(file);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }

        return jpeg is null ? null : Decode(bi => bi.StreamSource = new MemoryStream(jpeg));
    }

    /// <summary>
    /// Shared decode settings. <c>OnLoad</c> matters for both callers: it
    /// makes the bitmap independent of the stream (so the file handle and
    /// the buffer can go) and lets us freeze it for the UI thread.
    /// </summary>
    private static BitmapImage? Decode(Action<BitmapImage> setSource) {
        try {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            setSource(bi);
            bi.EndInit();
            bi.Freeze();

            return bi;
        } catch {
            // No codec for this format, truncated file, or a preview whose
            // bytes turned out not to be a JPEG after all.
            return null;
        }
    }

    private async Task LoadTextAsync(string path, CancellationToken ct) {
        if ((_primary?.Size ?? 0) > PreviewMaxFileSize) {
            Kind = PreviewKind.Unsupported;
            return;
        }

        string text;
        try {
            text = await File.ReadAllTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        } catch {
            Kind = PreviewKind.Unsupported;
            return;
        }

        if (ct.IsCancellationRequested) {
            return;
        }

        if (text.Length > PreviewMaxChars) {
            text = text.Substring(0, PreviewMaxChars) + "\n\n… (truncated)";
        }
        Text = text;
        Kind = PreviewKind.Text;
    }

    private async Task LoadCodeAsync(string path, string ext, CancellationToken ct) {
        if ((_primary?.Size ?? 0) > PreviewMaxFileSize) {
            Kind = PreviewKind.Unsupported;
            return;
        }

        string text;
        try {
            text = await File.ReadAllTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        } catch {
            Kind = PreviewKind.Unsupported;
            return;
        }

        if (ct.IsCancellationRequested) {
            return;
        }

        if (text.Length > PreviewMaxChars) {
            text = text.Substring(0, PreviewMaxChars) + "\n\n// … (truncated)";
        }
        CodeText = text;
        CodeExtension = ext;
        Kind = PreviewKind.Code;
    }

    private async Task LoadMarkdownAsync(string path, CancellationToken ct) {
        if ((_primary?.Size ?? 0) > PreviewMaxFileSize) {
            Kind = PreviewKind.Unsupported;
            return;
        }

        string md;
        try {
            md = await File.ReadAllTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        } catch {
            Kind = PreviewKind.Unsupported;
            return;
        }

        if (ct.IsCancellationRequested) {
            return;
        }

        string html = await Task.Run(() => Markdig.Markdown.ToHtml(md), ct);
        string wrapped = WrapHtml(html);
        WebHtml = wrapped;
        Kind = PreviewKind.Web;
    }

    private static string WrapHtml(string body) {
        return $@"<!doctype html><html><head><meta charset='utf-8'><style>
            body {{ font-family: 'Segoe UI', sans-serif; font-size: 13px; padding: 10px; color: #222; }}
            pre, code {{ font-family: Consolas, monospace; background: #f4f4f4; padding: 2px 4px; border-radius: 3px; }}
            pre {{ padding: 8px; overflow-x: auto; }}
            h1, h2, h3 {{ margin: 0.6em 0 0.3em; }}
            blockquote {{ border-left: 3px solid #ccc; margin: 0; padding-left: 10px; color: #555; }}
            table {{ border-collapse: collapse; }}
            th, td {{ border: 1px solid #ccc; padding: 4px 8px; }}
            img {{ max-width: 100%; }}
        </style></head><body>{body}</body></html>";
    }

    private void ClearPreviewContent() {
        Text = null;
        Image = null;
        CodeText = null;
        CodeExtension = null;
        WebUri = null;
        WebHtml = null;
        GifUri = null;
        VideoUri = null;
        ImageMetadata = null;
    }


    // --- Companion pipeline --------------------------------------------

    private void ScheduleCompanionUpdate() {
        _companionCts?.Cancel();
        _companionCts = new CancellationTokenSource();
        _ = UpdateCompanionsAsync(_companionCts.Token);
    }

    private async Task UpdateCompanionsAsync(CancellationToken ct) {
        ClearCompanionInfo();

        var companions = _primary?.Companions;
        if (!_isVisible || _companionMetadata is null || companions is null || companions.Count == 0) {
            return;
        }

        // Sidecars are tiny, but they still live on the same disk that can
        // be a sleeping spindle or a network share — off the UI thread like
        // every other read here.
        var loaded = await Task.Run(() => Load(companions), ct);
        if (ct.IsCancellationRequested) {
            return;
        }

        CompanionFiles = string.Join(", ", companions.Select(Path.GetFileName));
        UnityGuid = loaded.Meta?.Guid;
        UnityDetail = DescribeMeta(loaded.Meta);
        ShowRating(loaded.RatingPath, loaded.Rating);
    }

    private (UnityMetaInfo? Meta, string? RatingPath, SidecarRating? Rating) Load(IReadOnlyList<string> companions) {
        UnityMetaInfo? meta = null;
        string? ratingPath = null;
        SidecarRating? rating = null;

        foreach (string path in companions) {
            if (meta is null && Path.GetExtension(path).Equals(".meta", StringComparison.OrdinalIgnoreCase)) {
                meta = _companionMetadata!.ReadUnityMeta(path);
            } else if (ratingPath is null && CompanionMetadataService.IsRatingSidecar(path)) {
                rating = _companionMetadata!.ReadRating(path);
                if (rating is not null) {
                    ratingPath = path;
                }
            }
        }

        return (meta, ratingPath, rating);
    }

    /// <summary>Points the rating row at a sidecar (or at nothing) and refreshes what it shows.</summary>
    private void ShowRating(string? path, SidecarRating? rating) {
        _ratingPath = path;
        _colorLabel = rating?.ColorLabel ?? 0;
        Rank = rating?.Rank ?? 0;

        // A label an XMP spells its own way ("Client approved") maps to no
        // swatch, so say it in words instead of dropping it on the floor.
        CustomColorLabel = _colorLabel == 0 && !string.IsNullOrEmpty(rating?.ColorLabelName)
            ? rating!.ColorLabelName!
            : "";

        foreach (var choice in ColorLabelChoices) {
            choice.IsSelected = choice.Index == _colorLabel;
        }

        Raise(nameof(HasRating));
        Raise(nameof(RatingSource));
        SetRankCommand.RaiseCanExecuteChanged();
        SetColorLabelCommand.RaiseCanExecuteChanged();
        CopyGuidCommand.RaiseCanExecuteChanged();
    }

    private static string? DescribeMeta(UnityMetaInfo? meta) {
        if (meta is null) {
            return null;
        }

        var parts = new List<string>();
        if (meta.IsFolderAsset) {
            parts.Add("folder asset");
        }
        if (!string.IsNullOrEmpty(meta.Importer)) {
            parts.Add(meta.Importer!);
        }

        return parts.Count > 0 ? string.Join("   •   ", parts) : null;
    }

    private void SetRating(RatingField field, object? parameter, int current) {
        if (_ratingPath is null || _companionMetadata is null) {
            return;
        }
        if (!TryReadIndex(parameter, out int clicked)) {
            return;
        }

        // Clicking what is already set clears it — otherwise a mis-click
        // could never be taken back except through Ctrl+Z.
        int target = clicked == current ? 0 : clicked;
        try {
            _companionMetadata.SetRating(_ratingPath, field, target);
            CompanionStatus = "";
        } catch (Exception ex) {
            // Includes the deliberate refusals: no sidecar to write to, or
            // an XMP packet we won't add a property to.
            CompanionStatus = ex.Message;
        }

        // Re-read rather than assume: the write may have been refused, and
        // the file is the only thing that knows what it now says.
        ShowRating(_ratingPath, _companionMetadata.ReadRating(_ratingPath));
    }

    private static bool TryReadIndex(object? parameter, out int index) {
        index = 0;

        return parameter switch {
            int i => Set(i, out index),
            string s when int.TryParse(s, out int parsed) => Set(parsed, out index),
            _ => false,
        };

        static bool Set(int value, out int index) {
            index = value;

            return value is > 0 and <= 5;
        }
    }

    private void CopyGuid() {
        if (string.IsNullOrEmpty(_unityGuid)) {
            return;
        }
        try {
            System.Windows.Clipboard.SetText(_unityGuid);
            CompanionStatus = "GUID copied";
        } catch (Exception ex) {
            // The clipboard is a shared, lockable OS resource — another app
            // holding it is not a bug in ours.
            CompanionStatus = string.Format(Strings.StatusClipboardBusy, ex.Message);
        }
    }

    private void ClearCompanionInfo() {
        CompanionFiles = "";
        UnityGuid = null;
        UnityDetail = null;
        CompanionStatus = "";
        ShowRating(null, null);
    }


    // --- Footer summary -----------------------------------------------

    private async Task ShowFolderCensusAsync(string folder, CancellationToken ct) {
        FolderTitle = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        if (FolderTitle.Length == 0) {
            FolderTitle = folder;
        }
        FolderHeadline = Strings.PreviewCounting;
        FolderNote = "";
        FolderTypes.Clear();
        Kind = PreviewKind.Folder;
        IsLoading = true;

        FolderStats stats;
        try {
            var fs = ServiceLocator.Get<IFileSystem>();
            stats = await Task.Run(() => FolderStatistics.Collect(fs, folder, ct: ct), ct);
        } catch (OperationCanceledException) {
            // Superseded by a newer selection, which owns the spinner now —
            // clearing it here would blink the pane between the two.
            return;
        } catch {
            stats = FolderStats.Empty;
        }

        if (ct.IsCancellationRequested) {
            return;
        }

        FolderHeadline = string.Format(
            Strings.PreviewFolderHeadline,
            stats.Files,
            stats.Folders,
            SizeFormatter.Format(stats.TotalSize));
        // Deliberately vague about *which* budget stopped the walk (file
        // count, depth or folder count): the user needs to know the numbers
        // are a floor, and naming one limit when another one fired lies.
        FolderNote = stats.Truncated ? Strings.PreviewFolderTruncated : "";

        // Bars are relative to the biggest bucket, not to the total: with a
        // long tail every bar would otherwise be a hairline.
        long biggest = stats.Types.Count > 0 ? Math.Max(1, stats.Types[0].Size) : 1;
        foreach (var type in stats.Types) {
            FolderTypes.Add(new FolderTypeRow(
                type.Extension,
                string.Format(Strings.PreviewFolderTypeCount, type.Count),
                SizeFormatter.Format(type.Size),
                Math.Max(2, 90.0 * type.Size / biggest)));
        }

        IsLoading = false;
    }


    private void ScheduleSummaryUpdate() {
        _summaryCts?.Cancel();
        _summaryCts = new CancellationTokenSource();
        _ = UpdateSummaryAsync(_summaryCts.Token);
    }

    private async Task UpdateSummaryAsync(CancellationToken ct) {
        if (!_isVisible) {
            Summary = "";
            return;
        }

        // 1. Single file selected — show file details + EXIF if image.
        //    Recycle-bin items (OriginalLocation set) get "Deleted" instead of
        //    "Modified" and a second line with the source folder so the user
        //    can decide whether to restore them without context-switching.
        if (_selection.Count == 1 && _selection[0].Kind == EntryKind.File) {
            var e = _selection[0];
            string timeLabel = e.OriginalLocation is not null ? Strings.SummaryDeleted : Strings.SummaryModified;
            string summary = $"📄  {e.Name}\n{Strings.SummarySize}: {SizeFormatter.Format(e.Size)}   •   {timeLabel}: {FormatModified(e.ModifiedUtc)}";
            if (e.OriginalLocation is not null) {
                summary += $"\n{Strings.SummaryDeletedFrom}: {e.OriginalLocation}";
            }
            if (_imageMetadata is { } m) {
                summary += "\n" + FormatExif(m);
            }
            Summary = summary;
            return;
        }

        // 2. Single folder selected. Counts and sizes are the census
        //    panel's job now (it walks the tree once); repeating them here
        //    meant walking it twice and printing the same numbers twice.
        if (_selection.Count == 1 && _selection[0].Kind == EntryKind.Directory) {
            var e = _selection[0];
            Summary = e.OriginalLocation is not null
                ? $"📁  {e.Name}\n{Strings.SummaryDeleted}: {FormatModified(e.ModifiedUtc)}\n{Strings.SummaryDeletedFrom}: {e.OriginalLocation}"
                : $"📁  {e.Name}";
            return;
        }

        // 3. Multiple items selected. No census panel for a mixed
        //    selection, so the aggregate stays here.
        if (_selection.Count > 1) {
            Summary = string.Format(Strings.SummarySelectedCounting, _selection.Count);
            var paths = _selection.Select(en => en.FullPath).ToArray();
            var (count, size) = await Task.Run(() => CountAndSum(paths, ct), ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            Summary = string.Format(
                Strings.SummarySelected, _selection.Count, count, SizeFormatter.Format(size));
            return;
        }

        // 4. Nothing selected — the census panel above describes the folder
        //    we are standing in, so the footer only names it.
        if (!string.IsNullOrEmpty(_currentFolderPath)) {
            string name = string.IsNullOrEmpty(_currentFolderName) ? _currentFolderPath! : _currentFolderName;
            Summary = $"📁  {name}";
            return;
        }

        Summary = "";
    }

    private static (int Count, long Size) CountAndSum(string[] paths, CancellationToken ct) {
        int count = 0;
        long size = 0;
        foreach (var p in paths) {
            if (ct.IsCancellationRequested) {
                break;
            }
            try {
                if (Directory.Exists(p)) {
                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)) {
                        if (ct.IsCancellationRequested) {
                            break;
                        }
                        count++;
                        try {
                            size += new FileInfo(f).Length;
                        } catch {
                            // access denied per-file — ignore
                        }
                    }
                } else if (File.Exists(p)) {
                    count++;
                    try {
                        size += new FileInfo(p).Length;
                    } catch {
                        // ignore
                    }
                }
            } catch {
                // access denied on enumeration — skip this root
            }
        }
        return (count, size);
    }

    private static string FormatModified(DateTime utc) {
        return utc == DateTime.MinValue ? "—" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string FormatExif(ImageMetadata m) {
        var parts = new List<string>();
        string? camera = string.Join(" ", new[] { m.CameraMake, m.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(camera)) {
            parts.Add(camera);
        }
        var shot = new List<string>();
        if (!string.IsNullOrEmpty(m.IsoSpeed)) {
            shot.Add($"ISO {m.IsoSpeed}");
        }
        if (!string.IsNullOrEmpty(m.Aperture)) {
            shot.Add(m.Aperture);
        }
        if (!string.IsNullOrEmpty(m.ShutterSpeed)) {
            shot.Add(m.ShutterSpeed);
        }
        if (!string.IsNullOrEmpty(m.FocalLength)) {
            shot.Add(m.FocalLength);
        }
        if (shot.Count > 0) {
            parts.Add(string.Join(", ", shot));
        }
        if (m.PixelWidth is int w && m.PixelHeight is int h) {
            parts.Add($"{w} × {h}");
        }
        if (m.DateTaken is { } dt) {
            parts.Add(dt.ToString("yyyy-MM-dd HH:mm"));
        }
        return string.Join("   •   ", parts);
    }
}
