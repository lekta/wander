using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Wander.App.Preview;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Preview;
using Wander.Core.Shell;
using ImageMetadata = Wander.Core.Icons.ImageMetadata;


namespace Wander.App.Controllers;

/// <summary>
/// A star or a swatch was clicked in the preview footer. The pane only
/// knows which one; writing lives with the host's rating machinery —
/// asking before creating a sidecar, choosing the format, updating the row
/// without re-listing the folder. The handler answers through
/// <see cref="Rating"/>: what the sidecar says afterwards, or null when
/// nothing was written (declined, or nowhere to write) — the row is then
/// left unchanged.
/// </summary>
public sealed class RatingRequestedEventArgs : EventArgs {
    public RatingRequestedEventArgs(FileSystemEntry entry, RatingField field, int value) {
        Entry = entry;
        Field = field;
        Value = value;
    }


    public FileSystemEntry Entry { get; }
    public RatingField Field { get; }
    public int Value { get; }

    /// <summary>The handler's answer; null means nothing was written.</summary>
    public SidecarRating? Rating { get; set; }
}


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
///
/// <para>
/// What the pane cannot answer for itself it raises as an event instead of
/// calling the host: <see cref="RatingRequested"/> to write a rating,
/// <see cref="RevealRequested"/> to take the user to a path. The decisions
/// stay with the host, and the dependency points one way.
/// </para>
/// </summary>
public sealed class PreviewController : ObservableObject {
    /// <summary>
    /// Width a cover is decoded at. Twice what the card draws, so it stays
    /// sharp on a 200 % display without decoding a sleeve scan in full.
    /// </summary>
    private const int CoverDecodeWidth = 520;


    private readonly IImageMetadataReader? _metadataReader;
    private readonly CompanionMetadataService? _companionMetadata;

    private bool _isVisible;
    private FileSystemEntry? _primary;
    private IReadOnlyList<FileSystemEntry> _selection = Array.Empty<FileSystemEntry>();
    private string? _currentFolderPath;
    private string _currentFolderName = "";
    private string _folderHeadline = "";
    private string _folderTitle = "";
    private string _folderNote = "";

    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _summaryCts;

    private PreviewKind _kind = PreviewKind.None;
    private bool _isLoading;
    private bool _isCensusLoading;
    private string? _text;
    private ImageSource? _image;
    private bool _isRawImage;
    private bool _showRawDecode;
    private string? _codeText;
    private string? _codeExtension;
    private Uri? _webUri;
    private string? _webHtml;
    private Uri? _gifUri;
    private Uri? _mediaUri;
    private AudioTrackInfo? _audio;
    private IReadOnlyList<ModelPart> _modelParts = Array.Empty<ModelPart>();
    private Point3D _modelCenter;
    private double _modelRadius = 1;
    private string _modelDetail = "";
    private ImageSource? _audioCover;
    private string? _documentPath;
    private ImageMetadata? _imageMetadata;
    private string _summary = "";
    private string? _linkTarget;
    private bool _linkBroken;
    private VolumeInfo? _volume;

    private CancellationTokenSource? _companionCts;
    private string _companionFiles = "";
    private string _companionStatus = "";
    private string? _unityGuid;
    private string? _unityDetail;
    private string? _ratingPath;

    // The photo a star would have to create a sidecar for. Set only when
    // there is no sidecar yet; the two are never both meaningful.
    private string? _ratingTarget;
    private int _rank;
    private int _colorLabel;
    private string _customColorLabel = "";


    public PreviewController(
        IImageMetadataReader? metadataReader,
        CompanionMetadataService? companionMetadata) {

        _metadataReader = metadataReader;
        _companionMetadata = companionMetadata;

        ColorLabelChoices = ColorLabelViewModel.CreateChoices();

        SetRankCommand = new RelayCommand(p => SetRating(RatingField.Rank, p, _rank), _ => HasRating);
        SetColorLabelCommand = new RelayCommand(p => SetRating(RatingField.ColorLabel, p, _colorLabel), _ => HasRating);
        CopyGuidCommand = new RelayCommand(_ => CopyGuid(), _ => HasUnityGuid);
        GoToLinkTargetCommand = new RelayCommand(_ => GoToLinkTarget(), _ => HasLinkTarget);
    }


    /// <summary>
    /// A rating write is wanted; see <see cref="RatingRequestedEventArgs"/>.
    /// With no handler attached the rating row never offers itself.
    /// </summary>
    public event EventHandler<RatingRequestedEventArgs>? RatingRequested;

    /// <summary>
    /// The user wants to be taken to this path — navigate to its folder,
    /// select it, scroll it into view. Navigation is the host's, so the
    /// pane only says which path the button pointed at.
    /// </summary>
    public event EventHandler<string>? RevealRequested;


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

    /// <summary>
    /// The folder census is still walking the tree.
    ///
    /// <para>
    /// Separate from <see cref="IsLoading"/>, which raises a veil over the
    /// whole pane, because most of what the census pane shows is ready
    /// before the walk starts: the folder's name, and the drive's capacity
    /// when it is one. Covering those to say "counting" hides information
    /// the user already has in order to announce information they do not.
    /// The spinner goes where the numbers will appear, and says what it is
    /// waiting for.
    /// </para>
    /// </summary>
    public bool IsCensusLoading {
        get => _isCensusLoading;
        private set => SetField(ref _isCensusLoading, value);
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

    /// <summary>
    /// What the transport plays. One property for video and for audio,
    /// because it is one <c>MediaElement</c> and one set of play / pause /
    /// seek controls underneath both — an audio file is a video with
    /// nothing to draw, and giving it a transport of its own would have
    /// been a second copy of the same state machine.
    /// </summary>
    public Uri? MediaUri {
        get => _mediaUri;
        private set => SetField(ref _mediaUri, value);
    }

    /// <summary>
    /// What the file's tags say about the track. Null for everything that
    /// is not music, so the whole card binds its visibility to this.
    /// </summary>
    public AudioTrackInfo? Audio {
        get => _audio;
        private set {
            SetField(ref _audio, value);
            Raise(nameof(HasAudioText));
            Raise(nameof(AudioTitle));
            Raise(nameof(AudioArtist));
            Raise(nameof(AudioAlbum));
            Raise(nameof(AudioDetail));
            Raise(nameof(HasAudioArtist));
            Raise(nameof(HasAudioAlbum));
        }
    }

    /// <summary>The cover the file carries, decoded. Null when it carries none.</summary>
    public ImageSource? AudioCover {
        get => _audioCover;
        private set {
            SetField(ref _audioCover, value);
            Raise(nameof(HasAudioCover));
        }
    }

    public bool HasAudioCover => _audioCover is not null;

    public bool HasAudioText => _audio is not null;

    /// <summary>
    /// The title, falling back to the file name. A track whose tags were
    /// never filled in is still a track, and an empty headline over a
    /// transport says less than the name the user is looking at in the
    /// list.
    /// </summary>
    public string AudioTitle =>
        _audio?.Title ?? (_primary is null ? "" : Path.GetFileNameWithoutExtension(_primary.Name));

    public string AudioArtist => _audio?.Artist ?? "";

    public bool HasAudioArtist => !string.IsNullOrEmpty(_audio?.Artist);

    /// <summary>Album and year on one line — they are one fact about the release.</summary>
    public string AudioAlbum {
        get {
            if (_audio is null) {
                return "";
            }

            return _audio.Year is null
                ? _audio.Album ?? ""
                : _audio.Album is null ? _audio.Year : $"{_audio.Album} · {_audio.Year}";
        }
    }

    public bool HasAudioAlbum => AudioAlbum.Length > 0;

    /// <summary>
    /// The technical line under the name: bitrate, sampling rate, channels.
    /// Whatever of it the container actually stated — a missing figure is
    /// left out rather than printed as a zero.
    /// </summary>
    public string AudioDetail {
        get {
            if (_audio is null) {
                return "";
            }

            var parts = new List<string>();
            if (_audio.BitrateKbps is { } kbps and > 0) {
                parts.Add(string.Format(Strings.PreviewAudioBitrate, kbps));
            }
            if (_audio.SampleRate is { } rate and > 0) {
                parts.Add(string.Format(Strings.PreviewAudioSampleRate, rate / 1000.0));
            }
            if (_audio.Channels is { } channels and > 0) {
                parts.Add(channels == 1 ? Strings.PreviewAudioMono : Strings.PreviewAudioStereo);
            }

            return string.Join(" · ", parts);
        }
    }


    /// <summary>
    /// The model, ready for a <c>Viewport3D</c>: one drawable per material.
    /// Everything in it is frozen, because it is built on a worker thread
    /// and a live WPF object cannot cross to the dispatcher.
    /// </summary>
    public IReadOnlyList<ModelPart> ModelParts {
        get => _modelParts;
        private set {
            SetField(ref _modelParts, value);
            Raise(nameof(HasModel));
        }
    }

    public bool HasModel => _modelParts.Count > 0;

    /// <summary>Middle of the model's bounding box — what the camera looks at.</summary>
    public Point3D ModelCenter {
        get => _modelCenter;
        private set => SetField(ref _modelCenter, value);
    }

    /// <summary>
    /// Half the model's longest side. The camera distance and the lights
    /// are both scaled by it, so a model in millimetres and the same model
    /// in metres frame identically.
    /// </summary>
    public double ModelRadius {
        get => _modelRadius;
        private set => SetField(ref _modelRadius, value);
    }

    /// <summary>Triangle and vertex counts, for the footer under the viewport.</summary>
    public string ModelDetail {
        get => _modelDetail;
        private set => SetField(ref _modelDetail, value);
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
    /// Whether the rating row has anything to do: the selection carries a
    /// sidecar whose rating can be shown and edited (a RawTherapee
    /// <c>.pp3</c> or an XMP), or it is a picture that could have one.
    /// </summary>
    public bool HasRating => _ratingPath is not null || _ratingTarget is not null;

    /// <summary>
    /// True for a picture with no rating sidecar yet: the stars are there
    /// to be clicked, and the first click creates the file. Shown differently
    /// from a real rating — five hollow stars that mean "not rated" and five
    /// that mean "no file to rate into" are not the same statement.
    /// </summary>
    public bool IsRatingUnsaved => _ratingPath is null && _ratingTarget is not null;

    /// <summary>Name of the file the rating is read from and written to, for the tooltip.</summary>
    public string RatingSource => _ratingPath is not null ? Path.GetFileName(_ratingPath) : "";

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
    public Brush VolumeBarColor => (_volume?.UsedFraction ?? 0) switch {
        >= 0.95 => Palette.VolumeBarFull,
        >= 0.85 => Palette.VolumeBarFilling,
        _ => Palette.VolumeBarNormal,
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

        // The same file in a new row object. The listing replaces a row
        // whenever anything about it changes, and a rating written into its
        // sidecar is the everyday case — nothing the preview shows has moved,
        // so re-decoding a 30 MB RAW to draw the same picture would be a
        // second of nothing for no reason. Only the sidecar block is re-read.
        bool sameFile = entry is not null && _primary is not null
            && string.Equals(entry.FullPath, _primary.FullPath, StringComparison.OrdinalIgnoreCase)
            && entry.Size == _primary.Size
            && entry.ModifiedUtc == _primary.ModifiedUtc;

        _primary = entry;
        if (sameFile) {
            ScheduleCompanionUpdate();

            return;
        }

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
            if (PreviewRouter.Route(path) is PreviewRoute.Shortcut) {
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

        switch (PreviewRouter.Route(path)) {
            case PreviewRoute.Animation:
                LoadGif(path);
                break;

            case PreviewRoute.Video:
                LoadVideo(path);
                break;

            case PreviewRoute.Audio:
                await LoadAudioAsync(path, ct);
                break;

            case PreviewRoute.Model:
                await LoadModelAsync(path, ct);
                break;

            case PreviewRoute.Image:
                await LoadImageAsync(path, ct);
                break;

            case PreviewRoute.Web:
                WebUri = new Uri(path);
                Kind = PreviewKind.Web;
                break;

            case PreviewRoute.Book:
                await LoadBookAsync(path, ct);
                break;

            case PreviewRoute.Document:
                DocumentPath = path;
                Kind = PreviewKind.Document;
                break;

            case PreviewRoute.Markdown:
                await LoadMarkdownAsync(path, ct);
                break;

            case PreviewRoute.Code:
                await LoadCodeAsync(path, ext, ct);
                break;

            // Unity's serialised assets: text only when the project says
            // so, so the bytes are asked before the pane commits to
            // showing them.
            case PreviewRoute.MaybeText:
                if (await LooksLikeTextAsync(path, ct)) {
                    await LoadCodeAsync(path, ext, ct);
                } else {
                    Kind = PreviewKind.Unsupported;
                }
                break;

            case PreviewRoute.Text:
                await LoadTextAsync(path, ct);
                break;

            // A shortcut is resolved before we get here; one pointing at
            // another shortcut is where this lands, and there is nothing
            // to show for it.
            default:
                Kind = PreviewKind.Unsupported;
                break;
        }
    }


    /// <summary>
    /// Where a <c>.lnk</c> points, or null when there is no shortcut
    /// service registered or the file cannot be resolved.
    /// </summary>
    private static string? ResolveShortcut(string path) {
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
        //
        // Kind first, then the URI. The view picks which player to hand the
        // file to from the kind, so setting the URI while the kind still
        // says "folder" hands a track to the video element — which opens
        // it, reports its length and then never plays it.
        Kind = PreviewKind.Video;
        MediaUri = new Uri(path);
    }

    /// <summary>
    /// A music file: the same transport the video preview uses, plus what
    /// the container says about the track.
    ///
    /// <para>
    /// The playback itself needs nothing but the URI — Media Foundation
    /// reads both MP3 and FLAC on Windows 10 and later. The tags are ours
    /// to read (see <see cref="AudioTags"/>), which is why this one is
    /// async where <see cref="LoadVideo"/> is not: a cover can be a
    /// megabyte of JPEG, and that is a decode, on a file that may be on a
    /// network share.
    /// </para>
    /// </summary>
    private async Task LoadAudioAsync(string path, CancellationToken ct) {
        // Kind before the URI — see LoadVideo for why the order matters.
        Kind = PreviewKind.Audio;
        MediaUri = new Uri(path);

        AudioTrackInfo? info = null;
        BitmapImage? cover = null;

        await Task.Run(() => {
            ct.ThrowIfCancellationRequested();
            info = AudioTags.Read(path);

            if (info?.Cover is { Length: > 0 } bytes) {
                cover = ImageDecoder.Stream(bytes);
            } else if (AudioTags.CoverBeside(path) is { } beside) {
                // A sleeve scan next to the tracks is regularly several
                // megabytes, and it is about to be drawn at 260 px — so it
                // is decoded at that size rather than in full and thrown
                // away.
                cover = ImageDecoder.File(beside, CoverDecodeWidth);
            }
        }, ct);

        if (ct.IsCancellationRequested) {
            return;
        }

        Audio = info;
        AudioCover = cover;
    }


    /// <summary>
    /// A 3D model. Parsed in Core (see <see cref="MeshFile"/>) and turned
    /// into WPF geometry by <see cref="ModelBuilder"/>, both on the worker
    /// thread, because building a mesh of a million triangles on the
    /// dispatcher is a frozen window.
    /// </summary>
    private async Task LoadModelAsync(string path, CancellationToken ct) {
        ModelScene? scene = null;

        await Task.Run(() => {
            ct.ThrowIfCancellationRequested();
            var mesh = MeshFile.Read(path);
            if (mesh is not null) {
                scene = ModelBuilder.Build(mesh, ct);
            }
        }, ct);

        if (ct.IsCancellationRequested) {
            return;
        }
        if (scene is not { } model) {
            Kind = PreviewKind.Unsupported;

            return;
        }

        ModelCenter = model.Center;
        ModelRadius = model.Radius;
        ModelDetail = string.Format(Strings.PreviewModelDetail, model.Triangles, model.Vertices);
        ModelParts = model.Parts;
        Kind = PreviewKind.Model;
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
            if (ImageFormats.IsRaw(path)) {
                isRaw = true;
                var raw = _showRawDecode
                    ? ImageDecoder.File(path)
                    : ImageDecoder.RawPreview(path) ?? ImageDecoder.File(path);
                image = raw is null ? null : ImageDecoder.ApplyOrientation(raw, meta?.Orientation);

                return;
            }

            image = ImageDecoder.File(path);
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


    private async Task LoadTextAsync(string path, CancellationToken ct) {
        if (await ReadForPreviewAsync(path, ct) is not { } file) {
            return;
        }

        Text = PreviewText.Clip(file);
        Kind = PreviewKind.Text;
    }


    private async Task LoadCodeAsync(string path, string ext, CancellationToken ct) {
        if (await ReadForPreviewAsync(path, ct) is not { } file) {
            return;
        }

        CodeText = PreviewText.Clip(file, "// ");
        CodeExtension = ext;
        Kind = PreviewKind.Code;
    }


    private async Task LoadMarkdownAsync(string path, CancellationToken ct) {
        if (await ReadForPreviewAsync(path, ct) is not { } file) {
            return;
        }

        string html = await Task.Run(() => PreviewText.MarkdownToHtml(file), ct);
        WebHtml = PreviewText.WrapHtml(html);
        Kind = PreviewKind.Web;
    }


    /// <summary>
    /// The read every text-shaped loader starts with, and the two answers
    /// that end the load right there: a cancelled read (a newer selection
    /// owns the pane now) and a file that cannot be read at all. Null means
    /// the caller has nothing left to do.
    /// </summary>
    private async Task<PreviewTextFile?> ReadForPreviewAsync(string path, CancellationToken ct) {
        PreviewTextFile? read;
        try {
            read = await PreviewText.ReadAsync(path, ct);
        } catch (OperationCanceledException) {
            return null;
        }

        if (read is null) {
            Kind = PreviewKind.Unsupported;

            return null;
        }

        return ct.IsCancellationRequested ? null : read;
    }


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
        if (size > PreviewText.BookMaxFileSize) {
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

        WebHtml = PreviewText.WrapHtml(body, PreviewText.BookCss);
        Kind = PreviewKind.Web;
    }



    private void ClearPreviewContent() {
        Text = null;
        Image = null;
        CodeText = null;
        CodeExtension = null;
        WebUri = null;
        WebHtml = null;
        GifUri = null;
        MediaUri = null;
        Audio = null;
        AudioCover = null;
        ModelParts = Array.Empty<ModelPart>();
        ModelDetail = "";
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
            RevealRequested?.Invoke(this, target);
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

        if (!_isVisible || _companionMetadata is null || _primary is null) {
            return;
        }

        var companions = _primary.Companions;
        if (companions is null || companions.Count == 0) {
            // Nothing beside the file — but if it is a photograph, the stars
            // still appear, because "rate this raw" should not mean "go and
            // make it a sidecar first in another program".
            OfferRating(_primary);

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

        // A photo can have companions and still no place for a rating — a
        // Unity .meta next to a PNG is the everyday case.
        if (loaded.RatingPath is null) {
            OfferRating(_primary);
        }
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

    /// <summary>
    /// Offers the rating row for a picture that has no sidecar yet. Only
    /// pictures: a rating on a spreadsheet is a file nobody asked for, and
    /// the sidecar formats Wander writes are photo formats.
    /// </summary>
    private void OfferRating(FileSystemEntry entry) {
        if (RatingRequested is null || entry.IsFolderLike || !ImageFormats.IsImage(entry.Name)) {
            return;
        }

        _ratingTarget = entry.FullPath;
        Raise(nameof(HasRating));
        Raise(nameof(IsRatingUnsaved));
        SetRankCommand.RaiseCanExecuteChanged();
        SetColorLabelCommand.RaiseCanExecuteChanged();
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
        Raise(nameof(IsRatingUnsaved));
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
        if (RatingRequested is not { } write || _primary is null || !TryReadIndex(parameter, out int clicked)) {
            return;
        }

        // Clicking what is already set clears it — otherwise a mis-click
        // could never be taken back except through Ctrl+Z.
        int target = clicked == current ? 0 : clicked;

        var request = new RatingRequestedEventArgs(_primary, field, target);
        try {
            write(this, request);
            CompanionStatus = "";
        } catch (Exception ex) {
            // Includes the deliberate refusals: a sidecar that vanished
            // underneath us, or an XMP packet we won't add a property to.
            CompanionStatus = ex.Message;

            return;
        }

        if (request.Rating is null) {
            // Declined, or there was nowhere to write. Either is an answer
            // and not an error, and the row is unchanged.
            return;
        }

        // The write went through the host, which updates the row in the
        // listing; that comes back here as a new primary and re-reads the
        // sidecar. Showing the value we were handed keeps the stars from
        // lagging a frame behind the click in the meantime.
        _ratingTarget = null;
        ShowRating(_ratingPath ?? "", request.Rating);
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
        _ratingTarget = null;
        ShowRating(null, null);
    }


    // --- Footer summary -----------------------------------------------

    private async Task ShowFolderCensusAsync(string folder, CancellationToken ct) {
        FolderTitle = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        if (FolderTitle.Length == 0) {
            FolderTitle = folder;
        }
        // Blank rather than "counting": the spinner below says that, and
        // the numbers themselves start arriving within the first fraction
        // of a second, growing until the walk is done.
        FolderHeadline = "";
        FolderNote = "";
        FolderTypes.Clear();
        SetVolume(DescribeVolume(folder));
        Kind = PreviewKind.Folder;
        IsCensusLoading = true;

        // Built here, on the UI thread, so Progress<T> captures this
        // dispatcher and marshals the walk's reports back to it by itself.
        var progress = new Progress<FolderProgress>(p => {
            // A report posted before the walk was superseded can still be
            // waiting in the queue; it must not write the old folder's
            // numbers over the new one's.
            if (ct.IsCancellationRequested || !IsCensusLoading) {
                return;
            }
            FolderHeadline = FormatHeadline(p.Files, p.Folders, p.TotalSize);
        });

        FolderStats stats;
        try {
            var fs = ServiceLocator.Get<IFileSystem>();
            stats = await Task.Run(() => FolderStatistics.Collect(fs, folder, progress: progress, ct: ct), ct);
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

        // Cleared before the final numbers land, so a late progress report
        // finds the walk finished and stands aside.
        IsCensusLoading = false;

        FolderHeadline = FormatHeadline(stats.Files, stats.Folders, stats.TotalSize);
        // The only thing that stops a default walk now is the depth guard,
        // and that means a reparse-point loop rather than a big folder.
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
    }


    private static string FormatHeadline(int files, int folders, long totalSize) {
        return string.Format(
            Strings.PreviewFolderHeadline,
            files,
            folders,
            SizeFormatter.Format(totalSize));
    }


    /// <summary>
    /// The volume behind a folder, but only when the folder <em>is</em> the
    /// volume. A drive's capacity above the census of C:\Users would be
    /// answering about the disk while showing numbers about a folder.
    /// </summary>
    private static VolumeInfo? DescribeVolume(string folder) {
        if (ServiceLocator.TryGet<IVolumeInfoProvider>() is not { } volumes) {
            return null;
        }

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

        // 1. Single file selected — its details, plus EXIF if the metadata
        //    reader had something to say about it.
        if (_selection.Count == 1 && _selection[0].Kind == EntryKind.File) {
            Summary = SummaryText.ForFile(_selection[0], _imageMetadata);

            return;
        }

        // 2. Single folder selected. Counts and sizes are the census
        //    panel's job now (it walks the tree once); repeating them here
        //    meant walking it twice and printing the same numbers twice.
        if (_selection.Count == 1 && _selection[0].Kind == EntryKind.Directory) {
            Summary = SummaryText.ForFolder(_selection[0]);

            return;
        }

        // 3. Multiple items selected. No census panel for a mixed
        //    selection, so the aggregate stays here.
        if (_selection.Count > 1) {
            Summary = string.Format(Strings.SummarySelectedCounting, _selection.Count);
            var paths = _selection.Select(en => en.FullPath).ToArray();
            var (count, size) = await Task.Run(() => SummaryText.CountAndSum(paths, ct), ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            Summary = string.Format(
                Strings.SummarySelected, _selection.Count, count, SizeFormatter.Format(size));

            return;
        }

        // 4. Nothing selected — the census panel above describes the folder
        //    we are standing in, so the footer only names it.
        Summary = string.IsNullOrEmpty(_currentFolderPath)
            ? ""
            : SummaryText.ForCurrentFolder(_currentFolderPath!, _currentFolderName);
    }
}
