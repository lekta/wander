using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wander.App.Util;
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
    private string? _pp3Path;
    private int _pp3Rank;
    private int? _pp3ColorLabel;


    public PreviewController(IImageMetadataReader? metadataReader, CompanionMetadataService? companionMetadata) {
        _metadataReader = metadataReader;
        _companionMetadata = companionMetadata;

        SetRankCommand = new RelayCommand(p => SetRank(p), _ => HasPp3Rating);
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

    /// <summary>Whether the selection has a <c>.pp3</c> whose rating can be shown and edited.</summary>
    public bool HasPp3Rating => _pp3Path is not null;

    /// <summary>Stars currently written in the sidecar, 0…5.</summary>
    public int Pp3Rank {
        get => _pp3Rank;
        private set => SetField(ref _pp3Rank, value);
    }

    /// <summary>RawTherapee colour label, 0 (none) … 5. Null when the file doesn't say.</summary>
    public int? Pp3ColorLabel {
        get => _pp3ColorLabel;
        private set {
            if (SetField(ref _pp3ColorLabel, value)) {
                Raise(nameof(Pp3ColorLabelText));
            }
        }
    }

    public string Pp3ColorLabelText =>
        _pp3ColorLabel is int label && label > 0 ? $"Colour label {label}" : "";

    /// <summary>Writes a new star count into the <c>.pp3</c>. Parameter is the star clicked, 1…5.</summary>
    public RelayCommand SetRankCommand { get; }

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
        _kind == PreviewKind.None ? "Select a file to preview" : "No preview available";


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

        if (!_isVisible || _primary is null || _primary.Kind != EntryKind.File) {
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
            try {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                image = bi;
            } catch {
                // RAW or unsupported codec — image stays null, metadata may still load.
            }

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
        _pp3Path = loaded.Pp3Path;
        Pp3Rank = loaded.Rating?.Rank ?? 0;
        Pp3ColorLabel = loaded.Rating?.ColorLabel;
        Raise(nameof(HasPp3Rating));
        SetRankCommand.RaiseCanExecuteChanged();
        CopyGuidCommand.RaiseCanExecuteChanged();
    }

    private (UnityMetaInfo? Meta, string? Pp3Path, Pp3Rating? Rating) Load(IReadOnlyList<string> companions) {
        UnityMetaInfo? meta = null;
        string? pp3Path = null;
        Pp3Rating? rating = null;

        foreach (string path in companions) {
            string ext = Path.GetExtension(path);
            if (meta is null && ext.Equals(".meta", StringComparison.OrdinalIgnoreCase)) {
                meta = _companionMetadata!.ReadUnityMeta(path);
            } else if (pp3Path is null && ext.Equals(".pp3", StringComparison.OrdinalIgnoreCase)) {
                rating = _companionMetadata!.ReadPp3(path);
                if (rating is not null) {
                    pp3Path = path;
                }
            }
        }

        return (meta, pp3Path, rating);
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

    private void SetRank(object? parameter) {
        if (_pp3Path is null || _companionMetadata is null) {
            return;
        }
        if (parameter is not string raw || !int.TryParse(raw, out int star)) {
            return;
        }

        // Clicking the star that already marks the rating clears it —
        // otherwise a mis-click could never be taken back without Ctrl+Z.
        int target = star == Pp3Rank ? 0 : star;
        try {
            _companionMetadata.SetPp3Rank(_pp3Path, target);
            Pp3Rank = target;
            CompanionStatus = "";
        } catch (Exception ex) {
            CompanionStatus = ex.Message;
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
            CompanionStatus = $"Clipboard is busy: {ex.Message}";
        }
    }

    private void ClearCompanionInfo() {
        CompanionFiles = "";
        UnityGuid = null;
        UnityDetail = null;
        _pp3Path = null;
        Pp3Rank = 0;
        Pp3ColorLabel = null;
        CompanionStatus = "";
        Raise(nameof(HasPp3Rating));
        SetRankCommand.RaiseCanExecuteChanged();
        CopyGuidCommand.RaiseCanExecuteChanged();
    }


    // --- Footer summary -----------------------------------------------

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
            string timeLabel = e.OriginalLocation is not null ? "Deleted" : "Modified";
            string summary = $"📄  {e.Name}\nSize: {SizeFormatter.Format(e.Size)}   •   {timeLabel}: {FormatModified(e.ModifiedUtc)}";
            if (e.OriginalLocation is not null) {
                summary += $"\nDeleted from: {e.OriginalLocation}";
            }
            if (_imageMetadata is { } m) {
                summary += "\n" + FormatExif(m);
            }
            Summary = summary;
            return;
        }

        // 2. Single folder selected — recursive count + size, async.
        //    Recycled folders skip the recursion: their on-disk path under
        //    $Recycle.Bin\$R… may not be reliably enumerable, and the user
        //    cares about origin + delete time, not "how many files inside".
        if (_selection.Count == 1 && _selection[0].Kind == EntryKind.Directory) {
            var e = _selection[0];
            if (e.OriginalLocation is not null) {
                Summary = $"📁  {e.Name}\nDeleted: {FormatModified(e.ModifiedUtc)}\nDeleted from: {e.OriginalLocation}";
                return;
            }
            Summary = $"📁  {e.Name} — calculating…";
            var (count, size) = await Task.Run(() => CountAndSum(new[] { e.FullPath }, ct), ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            Summary = $"📁  {e.Name} — {count} files, {SizeFormatter.Format(size)}";
            return;
        }

        // 3. Multiple items selected.
        if (_selection.Count > 1) {
            Summary = $"{_selection.Count} items selected — calculating…";
            var paths = _selection.Select(en => en.FullPath).ToArray();
            var (count, size) = await Task.Run(() => CountAndSum(paths, ct), ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            Summary = $"{_selection.Count} items selected — {count} files inside, {SizeFormatter.Format(size)}";
            return;
        }

        // 4. Nothing selected — summary of current folder.
        if (!string.IsNullOrEmpty(_currentFolderPath)) {
            string name = string.IsNullOrEmpty(_currentFolderName) ? _currentFolderPath! : _currentFolderName;
            Summary = $"📁  {name} — calculating…";
            string cur = _currentFolderPath!;
            var (count, size) = await Task.Run(() => CountAndSum(new[] { cur }, ct), ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            Summary = $"📁  {name} — {count} files, {SizeFormatter.Format(size)}";
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
