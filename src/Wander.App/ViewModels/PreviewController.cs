using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Preview;
using Wander.Core.Shell;
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
        ".sh", ".bash", ".zsh", ".ps1", ".bat", ".cmd",
        ".sql",
        ".xml", ".xaml", ".svg",
        ".json", ".yaml", ".yml",
        // Patches — AvalonEdit's own "Patch" definition colours these, so
        // routing them here is the whole feature.
        ".diff", ".patch",
        // Unity shaders and their includes; highlighting comes from
        // Highlighting/ShaderLab.xshd.
        ".shader", ".cginc", ".hlsl", ".compute",
    };

    /// <summary>
    /// Extensions that mean text in one project and an opaque blob in the
    /// next — Unity's serialised assets, which are YAML only when the
    /// project forces text serialization. The bytes decide; see
    /// <see cref="TextProbe"/>.
    /// </summary>
    private static readonly HashSet<string> _maybeTextExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".asset", ".prefab", ".unity", ".mat",
    };

    /// <summary>
    /// Handed straight to WebView2 by path. PDF and HTML have always been
    /// here; MHTML joins them because Chromium reads the format natively,
    /// which is the whole reason a saved web page can be previewed at all.
    /// </summary>
    private static readonly HashSet<string> _webExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".pdf", ".html", ".htm", ".mht", ".mhtml",
    };

    private const long PreviewMaxFileSize = 1_048_576;     // 1 MB
    private const int PreviewMaxChars = 200_000;

    /// <summary>
    /// Books get a budget of their own: a novel is legitimately tens of
    /// megabytes once its illustrations are counted, and the 1 MB ceiling
    /// the text preview uses would refuse most of a shelf.
    /// </summary>
    private const long BookMaxFileSize = 64L * 1024 * 1024;


    private readonly IImageMetadataReader? _metadataReader;
    private readonly CompanionMetadataService? _companionMetadata;
    private readonly Action<string>? _reveal;

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
    private bool _isRawImage;
    private bool _showRawDecode;
    private string? _codeText;
    private string? _codeExtension;
    private Uri? _webUri;
    private string? _webHtml;
    private Uri? _gifUri;
    private Uri? _videoUri;
    private string? _documentPath;
    private ImageMetadata? _imageMetadata;
    private string _summary = "";
    private string? _linkTarget;
    private bool _linkBroken;
    private VolumeInfo? _volume;

    private CancellationTokenSource? _companionCts;
    private string _companionFiles = "";
    private string? _unityGuid;
    private string? _unityDetail;
    private string? _ratingPath;
    private int _rank;
    private int _colorLabel;
    private string _customColorLabel = "";


    /// <param name="reveal">
    /// How to take the user to a path — navigate to its folder, select it,
    /// scroll it into view. Owned by the view model, because navigation is;
    /// the pane only knows which path the button should point at.
    /// </param>
    public PreviewController(
        IImageMetadataReader? metadataReader,
        CompanionMetadataService? companionMetadata,
        Action<string>? reveal = null) {

        _metadataReader = metadataReader;
        _companionMetadata = companionMetadata;
        _reveal = reveal;

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
        GoToLinkTargetCommand = new RelayCommand(_ => GoToLinkTarget(), _ => HasLinkTarget);
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

    /// <summary>
    /// The file on screen is a RAW — the only case where there are two
    /// pictures to choose between, so the only case that shows the switch.
    /// </summary>
    public bool IsRawImage {
        get => _isRawImage;
        private set => SetField(ref _isRawImage, value);
    }

    /// <summary>
    /// Show the sensor decode instead of the preview the camera embedded.
    /// The embedded one is what makes the pane instant (~10 ms against
    /// ~1150 ms) but it is a small JPEG the camera baked with its own
    /// rendering; the sensor decode is the actual frame. A mode rather than
    /// a per-file choice — someone comparing shots wants it to stay on —
    /// and the button stays lit to say why the pane got slow.
    /// </summary>
    public bool ShowRawDecode {
        get => _showRawDecode;
        set {
            if (SetField(ref _showRawDecode, value)) {
                SchedulePreviewUpdate();
            }
        }
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

    /// <summary>
    /// File for the rich-text viewer (<c>.rtf</c>). A path rather than a
    /// document: WPF parses RTF itself, but only into a
    /// <c>FlowDocument</c>, which is a UI object and belongs on the other
    /// side of the binding.
    /// </summary>
    public string? DocumentPath {
        get => _documentPath;
        private set => SetField(ref _documentPath, value);
    }

    /// <summary>
    /// What the previewed <c>.lnk</c> points at, when the selection is a
    /// shortcut and its target still exists. The pane shows the target's
    /// content, so the footer has to say whose content it is — and offer
    /// the way over to it.
    /// </summary>
    public string? LinkTarget {
        get => _linkTarget;
        private set {
            if (SetField(ref _linkTarget, value)) {
                Raise(nameof(HasLinkTarget));
                Raise(nameof(LinkTargetName));
                GoToLinkTargetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasLinkTarget => !string.IsNullOrEmpty(_linkTarget);

    /// <summary>Target's file name, for the button caption.</summary>
    public string LinkTargetName => _linkTarget is null ? "" : Path.GetFileName(_linkTarget.TrimEnd(Path.DirectorySeparatorChar));

    /// <summary>Goes to the file the shortcut points at: its folder, selected and scrolled to.</summary>
    public RelayCommand GoToLinkTargetCommand { get; }

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
        _kind == PreviewKind.None ? Strings.PreviewSelectFile
        : _linkBroken ? Strings.PreviewLinkBroken
        : Strings.PreviewUnsupported;


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


    // --- Volume block ---------------------------------------------------
    // A drive root is a folder like any other as far as the census goes,
    // but "what is in here" is the wrong first question about a disk. The
    // one the user is actually asking — how full is it, and what is it —
    // is answered above the census, from the volume itself rather than
    // from a walk.

    /// <summary>True when the folder on screen is the root of a volume.</summary>
    public bool HasVolume => _volume is not null;

    /// <summary>Volume label, or the drive letter when the volume is unnamed.</summary>
    public string VolumeLabel => _volume is null
        ? ""
        : _volume.Label.Length > 0 ? _volume.Label : _volume.Root;

    /// <summary>File system and kind: "NTFS · Локальный диск".</summary>
    public string VolumeDetail {
        get {
            if (_volume is null) {
                return "";
            }
            if (!_volume.IsReady) {
                return Strings.PreviewVolumeNotReady;
            }

            var parts = new List<string>();
            if (_volume.FileSystem.Length > 0) {
                parts.Add(_volume.FileSystem);
            }
            parts.Add(DescribeKind(_volume.Kind));

            return string.Join("   •   ", parts);
        }
    }

    /// <summary>"Занято 412 GB из 931 GB" — the headline number for a disk.</summary>
    public string VolumeUsage => _volume is not { IsReady: true, TotalBytes: > 0 }
        ? ""
        : string.Format(
            Strings.PreviewVolumeUsage,
            SizeFormatter.Format(_volume.UsedBytes),
            SizeFormatter.Format(_volume.TotalBytes));

    /// <summary>"Свободно 519 GB" — the number people actually go looking for.</summary>
    public string VolumeFree => _volume is not { IsReady: true, TotalBytes: > 0 }
        ? ""
        : string.Format(Strings.PreviewVolumeFree, SizeFormatter.Format(_volume.FreeBytes));

    /// <summary>Width of the filled part of the capacity bar, as a percentage of the track.</summary>
    public double VolumeUsedPercent => (_volume?.UsedFraction ?? 0) * 100;

    /// <summary>
    /// The bar turns amber and then red as the disk fills. Explorer does
    /// the same thing and it is the one piece of colour on this panel that
    /// carries information rather than decoration.
    /// </summary>
    public string VolumeBarColor => (_volume?.UsedFraction ?? 0) switch {
        >= 0.95 => "#D13438",
        >= 0.85 => "#CA8A00",
        _ => "#0078D7",
    };


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

            // A shortcut is a file about another file. Nobody opens the
            // preview pane to look at a .lnk, so it stands aside and the
            // target is previewed in its place — with the footer saying
            // whose content is on screen and offering the way over.
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
                string? resolved = ResolveShortcut(path);
                if (resolved is null) {
                    Kind = PreviewKind.Unsupported;
                    return;
                }

                LinkTarget = resolved;
                if (Directory.Exists(resolved)) {
                    await ShowFolderCensusAsync(resolved, ct);
                    return;
                }
                if (!File.Exists(resolved)) {
                    // A shortcut whose target has been moved or deleted.
                    // Worth saying in those words: "no preview for this
                    // file" would blame the wrong file.
                    _linkBroken = true;
                    Kind = PreviewKind.Unsupported;
                    return;
                }

                path = resolved;
            }

            await LoadFileAsync(path, ct);
        } catch (OperationCanceledException) {
            // newer selection won — ignore
        } finally {
            if (!ct.IsCancellationRequested) {
                IsLoading = false;
                ScheduleSummaryUpdate();  // metadata might have arrived
            }
        }
    }

    /// <summary>
    /// Picks a renderer for one file. Split out of the update pass because
    /// it is also where a shortcut's target lands — the dispatch has to be
    /// the same whether the user selected the file or something pointing
    /// at it.
    /// </summary>
    private async Task LoadFileAsync(string path, CancellationToken ct) {
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

        if (_webExtensions.Contains(ext)) {
            WebUri = new Uri(path);
            Kind = PreviewKind.Web;

            return;
        }

        if (ext.Equals(".fb2", StringComparison.OrdinalIgnoreCase)) {
            await LoadBookAsync(path, ct);

            return;
        }

        if (ext.Equals(".rtf", StringComparison.OrdinalIgnoreCase)) {
            DocumentPath = path;
            Kind = PreviewKind.Document;

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

        // Unity's serialised assets: text only when the project says so,
        // so the bytes are asked before the pane commits to showing them.
        if (_maybeTextExtensions.Contains(ext)) {
            if (await LooksLikeTextAsync(path, ct)) {
                await LoadCodeAsync(path, ext, ct);
            } else {
                Kind = PreviewKind.Unsupported;
            }

            return;
        }

        if (_textExtensions.Contains(ext) || string.IsNullOrEmpty(ext)) {
            await LoadTextAsync(path, ct);

            return;
        }

        Kind = PreviewKind.Unsupported;
    }


    /// <summary>
    /// Where a <c>.lnk</c> points, or null when there is no shortcut
    /// service registered or the file cannot be resolved.
    /// </summary>
    private static string? ResolveShortcut(string path) {
        if (!ServiceLocator.IsRegistered<IShortcutService>()) {
            return null;
        }

        try {
            string? target = ServiceLocator.Get<IShortcutService>().Resolve(path);

            return string.IsNullOrEmpty(target) ? null : target;
        } catch {
            // A .lnk to a shell namespace ("This PC"), or a malformed one.
            return null;
        }
    }


    private static async Task<bool> LooksLikeTextAsync(string path, CancellationToken ct) {
        try {
            using var file = File.OpenRead(path);
            var head = new byte[TextProbe.SampleSize];
            int read = await file.ReadAsync(head.AsMemory(), ct);

            return TextProbe.LooksLikeText(head.AsSpan(0, read));
        } catch (OperationCanceledException) {
            return false;
        } catch {
            return false;
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
        BitmapSource? image = null;
        ImageMetadata? meta = null;
        bool isRaw = false;

        await Task.Run(() => {
            ct.ThrowIfCancellationRequested();
            if (_metadataReader is not null) {
                meta = _metadataReader.Read(path);
            }

            // RAW comes out of its container unrotated, whichever way we
            // read it: the embedded preview carries no EXIF of its own, and
            // WIC's own RAW decode ignores the orientation tag too. A camera
            // set to "rotate on the computer only" records the rotation in
            // the container's IFD0 and leaves the pixels alone — so this is
            // the one branch that has to apply it, and it applies to the
            // full-decode fallback as well.
            //
            // JPEG and PNG are deliberately left as they are: what they look
            // like everywhere else is what the user expects to see here.
            if (_rawExtensions.Contains(Path.GetExtension(path))) {
                isRaw = true;
                var raw = _showRawDecode ? DecodeFile(path) : LoadRawPreview(path) ?? DecodeFile(path);
                image = raw is null ? null : ApplyOrientation(raw, meta?.Orientation);

                return;
            }

            image = DecodeFile(path);
        }, ct);

        if (ct.IsCancellationRequested) {
            return;
        }

        ImageMetadata = meta;
        IsRawImage = isRaw;
        if (image is not null) {
            Image = image;
            Kind = PreviewKind.Image;
        } else {
            Kind = PreviewKind.Unsupported;
        }
    }


    /// <summary>
    /// Turns an EXIF orientation value (1..8) into the rotation and mirror
    /// it stands for. Values Wander cannot act on — and the identity value
    /// 1 — return the bitmap untouched.
    /// </summary>
    private static BitmapSource ApplyOrientation(BitmapSource source, int? orientation) {
        var transform = orientation switch {
            2 => Mirror(0),
            3 => new RotateTransform(180),
            4 => Mirror(180),
            5 => Mirror(90),
            6 => new RotateTransform(90),
            7 => Mirror(270),
            8 => new RotateTransform(270),
            _ => (Transform?)null,
        };
        if (transform is null) {
            return source;
        }

        var rotated = new TransformedBitmap(source, transform);
        rotated.Freeze();

        return rotated;
    }

    /// <summary>Horizontal flip, then <paramref name="degrees"/> of rotation.</summary>
    private static Transform Mirror(double degrees) {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(-1, 1));
        group.Children.Add(new RotateTransform(degrees));

        return group;
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

        return jpeg is null ? null : DecodeStream(jpeg);
    }

    /// <summary>
    /// Decodes a file by path. <c>IgnoreImageCache</c> is what keeps a
    /// re-opened file from showing its previous contents — WPF's image
    /// cache is keyed by URI and does not notice the bytes changed.
    /// </summary>
    private static BitmapImage? DecodeFile(string path) {
        return Decode(bi => {
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.UriSource = new Uri(path);
        });
    }

    /// <summary>
    /// Decodes bytes already in memory — the preview pulled out of a RAW
    /// container.
    ///
    /// <para>
    /// Deliberately without <c>IgnoreImageCache</c>: that flag makes
    /// <c>BitmapImage.FinalizeCreation</c> evict the URI it was loaded
    /// from, and a stream-sourced bitmap has no URI — on .NET 10 that is an
    /// <c>ArgumentNullException</c> from inside WPF. It cost the whole RAW
    /// fast path: the decode threw, the caller read the <c>null</c> as "no
    /// embedded preview" and quietly fell back to a full sensor decode.
    /// The flag is meaningless here anyway — there is no cache entry to
    /// bypass when the source is a private <c>MemoryStream</c>.
    /// </para>
    /// </summary>
    private static BitmapImage? DecodeStream(byte[] bytes) {
        return Decode(bi => bi.StreamSource = new MemoryStream(bytes));
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

    /// <summary>
    /// The file's own size, not the selected row's: when the selection is a
    /// shortcut, the row is a two-kilobyte <c>.lnk</c> and the thing about
    /// to be read is whatever it points at.
    /// </summary>
    private static bool TooBigForText(string path) {
        try {
            return new FileInfo(path).Length > PreviewMaxFileSize;
        } catch {
            // Gone, or unreadable — the read below will say so properly.
            return false;
        }
    }

    /// <summary>
    /// Reads a text file, working out its encoding rather than assuming
    /// UTF-8. Assuming turns every byte of a codepaged file into
    /// <c>U+FFFD</c>, and a folder of old notes reads as a wall of black
    /// diamonds — see <see cref="EncodingProbe"/>.
    /// </summary>
    private static async Task<string?> ReadTextAsync(string path, CancellationToken ct) {
        try {
            byte[] bytes = await File.ReadAllBytesAsync(path, ct);

            return EncodingProbe.Decode(bytes);
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return null;
        }
    }

    private async Task LoadTextAsync(string path, CancellationToken ct) {
        if (TooBigForText(path)) {
            Kind = PreviewKind.Unsupported;
            return;
        }

        string? text;
        try {
            text = await ReadTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        }
        if (text is null) {
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
        if (TooBigForText(path)) {
            Kind = PreviewKind.Unsupported;
            return;
        }

        string? text;
        try {
            text = await ReadTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        }
        if (text is null) {
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
        if (TooBigForText(path)) {
            Kind = PreviewKind.Unsupported;
            return;
        }

        string? md;
        try {
            md = await ReadTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        }
        if (md is null) {
            Kind = PreviewKind.Unsupported;
            return;
        }

        if (ct.IsCancellationRequested) {
            return;
        }

        string html = await Task.Run(() => Markdown.ToHtml(md, _markdownPipeline), ct);
        string wrapped = WrapHtml(html);
        WebHtml = wrapped;
        Kind = PreviewKind.Web;
    }


    /// <summary>
    /// Markdig speaks plain CommonMark unless told otherwise, and CommonMark
    /// has no tables — a <c>| … | … |</c> block came out as one run-on
    /// paragraph of pipes and dashes. Which is most of what a README's
    /// tables are for.
    ///
    /// <para>
    /// Listed one by one rather than through <c>UseAdvancedExtensions()</c>:
    /// that bundle also turns YouTube links into iframes and reads
    /// <c>{#id .class}</c> out of the text as markup, neither of which a
    /// preview pane wants — least of all one that blocks the network and
    /// would show the iframe as an empty box.
    /// </para>
    /// </summary>
    private static readonly MarkdownPipeline _markdownPipeline =
        new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .UseEmphasisExtras()      // ~~strikethrough~~, ++inserted++
            .UseTaskLists()           // - [x] done
            .UseAutoLinks()           // bare https://… as a link
            .UseFootnotes()
            .Build();


    /// <summary>
    /// FictionBook. Parsed in Core into an HTML fragment and shown through
    /// the same WebView2 the PDF and Markdown previews use — the format is
    /// XML, so there is nothing to install and nothing to shell out to.
    /// </summary>
    private async Task LoadBookAsync(string path, CancellationToken ct) {
        long size;
        try {
            size = new FileInfo(path).Length;
        } catch {
            Kind = PreviewKind.Unsupported;

            return;
        }
        if (size > BookMaxFileSize) {
            Kind = PreviewKind.Unsupported;

            return;
        }

        Fb2Preview? book;
        try {
            book = await Task.Run(() => {
                using var file = File.OpenRead(path);

                return Fb2Document.Read(file);
            }, ct);
        } catch (OperationCanceledException) {
            return;
        } catch {
            Kind = PreviewKind.Unsupported;

            return;
        }

        if (ct.IsCancellationRequested) {
            return;
        }
        if (book is null) {
            Kind = PreviewKind.Unsupported;

            return;
        }

        string body = book.Truncated
            ? book.BodyHtml + $"<p class='fb2-cut'>{Strings.PreviewBookTruncated}</p>"
            : book.BodyHtml;

        WebHtml = WrapHtml(body, BookCss);
        Kind = PreviewKind.Web;
    }


    /// <summary>
    /// Book-specific rules on top of the shared ones: a cover that sits at
    /// a plate's size rather than filling the pane, and the indented,
    /// centred shapes FB2 uses for verse and epigraphs.
    /// </summary>
    private const string BookCss = @"
        .fb2-head { text-align: center; margin-bottom: 1.5em; }
        .fb2-cover { max-width: 220px; max-height: 320px; box-shadow: 0 1px 6px rgba(0,0,0,.35); margin-bottom: 10px; }
        .fb2-head h1 { font-size: 18px; margin: 0.2em 0; }
        .fb2-author { color: #555; margin: 0.2em 0 0; }
        .fb2-annotation { text-align: left; font-size: 12px; color: #444; border-top: 1px solid #DDD; margin-top: 12px; padding-top: 8px; }
        .fb2-title { font-size: 15px; font-weight: 600; margin: 1.2em 0 0.5em; }
        .fb2-title p { margin: 0; }
        .fb2-empty { height: 0.8em; }
        .fb2-poem { margin: 1em 2em; font-style: italic; }
        .fb2-stanza { margin-bottom: 0.8em; }
        .fb2-text-author { text-align: right; color: #555; font-style: italic; }
        .fb2-image { display: block; margin: 1em auto; max-width: 100%; }
        .fb2-cut { color: #A05000; border-top: 1px solid #DDD; padding-top: 8px; }
        p { text-indent: 1.2em; margin: 0.2em 0; text-align: justify; }
        blockquote p { text-indent: 0; }";

    private static string WrapHtml(string body, string extraCss = "") {
        return $@"<!doctype html><html><head><meta charset='utf-8'><style>
            body {{ font-family: 'Segoe UI', sans-serif; font-size: 13px; padding: 10px; color: #222; }}
            pre, code {{ font-family: Consolas, monospace; background: #f4f4f4; padding: 2px 4px; border-radius: 3px; }}
            pre {{ padding: 8px; overflow-x: auto; }}
            h1, h2, h3 {{ margin: 0.6em 0 0.3em; }}
            blockquote {{ border-left: 3px solid #ccc; margin: 0; padding-left: 10px; color: #555; }}
            /* display:block so a table wider than the pane scrolls inside
               itself instead of pushing the whole page sideways. */
            table {{ border-collapse: collapse; display: block; overflow-x: auto; max-width: 100%; }}
            th, td {{ border: 1px solid #ccc; padding: 4px 8px; text-align: left; }}
            th {{ background: #F0F0F0; }}
            img {{ max-width: 100%; }}
            ul.contains-task-list {{ list-style: none; padding-left: 1.2em; }}
            {extraCss}
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
        DocumentPath = null;
        ImageMetadata = null;
        IsRawImage = false;
        LinkTarget = null;
        _linkBroken = false;
        SetVolume(null);
    }


    /// <summary>
    /// Takes the user to the file the previewed shortcut points at. The
    /// pane can already show it; this is for when looking is not enough and
    /// they want to be standing next to it.
    /// </summary>
    private void GoToLinkTarget() {
        if (_linkTarget is { } target) {
            _reveal?.Invoke(target);
        }
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
        SetVolume(DescribeVolume(folder));
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


    /// <summary>
    /// The volume behind a folder, but only when the folder <em>is</em> the
    /// volume. A drive's capacity above the census of C:\Users would be
    /// answering about the disk while showing numbers about a folder.
    /// </summary>
    private static VolumeInfo? DescribeVolume(string folder) {
        if (!ServiceLocator.IsRegistered<IVolumeInfoProvider>()) {
            return null;
        }

        var volumes = ServiceLocator.Get<IVolumeInfoProvider>();

        return volumes.IsVolumeRoot(folder) ? volumes.Describe(folder) : null;
    }

    private void SetVolume(VolumeInfo? volume) {
        if (_volume == volume) {
            return;
        }

        _volume = volume;
        Raise(nameof(HasVolume));
        Raise(nameof(VolumeLabel));
        Raise(nameof(VolumeDetail));
        Raise(nameof(VolumeUsage));
        Raise(nameof(VolumeFree));
        Raise(nameof(VolumeUsedPercent));
        Raise(nameof(VolumeBarColor));
    }

    private static string DescribeKind(VolumeKind kind) {
        return kind switch {
            VolumeKind.Fixed => Strings.VolumeKindFixed,
            VolumeKind.Removable => Strings.VolumeKindRemovable,
            VolumeKind.Network => Strings.VolumeKindNetwork,
            VolumeKind.Optical => Strings.VolumeKindOptical,
            VolumeKind.Ram => Strings.VolumeKindRam,
            _ => Strings.VolumeKindUnknown,
        };
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
