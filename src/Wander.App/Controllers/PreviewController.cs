using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Wander.App.Converters;
using Wander.App.Preview;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Companions;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Preview;
using Wander.Core.Shell;
using ImageMetadata = Wander.Core.Icons.ImageMetadata;


namespace Wander.App.Controllers;

/// <summary>
/// A star or a swatch was clicked in the preview footer. The pane only
/// knows which one; writing lives with the host's rating machinery —
/// asking before creating a sidecar, choosing the format, updating the row
/// without re-listing the folder, and deciding whether the click sets or
/// clears (<c>RatingToggle</c>: the host knows how many files the click is
/// about, the pane does not). The handler answers through
/// <see cref="Rating"/>: what the sidecar says afterwards, or null when
/// nothing was written (declined, or nowhere to write) — the row is then
/// left unchanged.
/// </summary>
public sealed class RatingRequestedEventArgs : EventArgs {
    public RatingRequestedEventArgs(FileSystemEntry entry, RatingField field, int clicked, int current) {
        Entry = entry;
        Field = field;
        Clicked = clicked;
        Current = current;
    }


    /// <summary>The file on screen when the click happened.</summary>
    public FileSystemEntry Entry { get; }
    public RatingField Field { get; }

    /// <summary>The star or the swatch, 1…5 — not yet resolved into "set" or "clear".</summary>
    public int Clicked { get; }

    /// <summary>What the pane shows for <see cref="Entry"/> in that field right now, 0 for nothing.</summary>
    public int Current { get; }

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

    /// <summary>
    /// How many rows of an archive the pane lists. Past this the answer to
    /// "what is in here" is the count, not the names, and every row costs
    /// a shell call for its icon.
    /// </summary>
    private const int ArchiveRowLimit = 200;

    /// <summary>
    /// How big an entry the pane will unpack to look inside. Above it the
    /// wait and the disk both stop being worth a glance, and "Open" is
    /// there for the times they are.
    /// </summary>
    private const long MaxArchivePreviewBytes = 32L * 1024 * 1024;


    /// <summary>
    /// How many pictures of a multi-selection are opened for the shared
    /// EXIF line. Reading a header is cheap; reading two thousand of them
    /// off a spinning disk because somebody pressed Ctrl+A is not, and
    /// the line says how many it looked at.
    /// </summary>
    private const int ShotSummarySample = 100;

    /// <summary>
    /// How long a load runs before the veil and its spinner go up. Most
    /// loads are over sooner, and a veil that flashes for a tenth of a
    /// second on every arrow key through a folder of photographs is the
    /// only thing on screen that says "slow" (2026-09-21). The listing
    /// waits 150 ms for the same reason; a picture takes a little longer.
    /// </summary>
    private const int VeilDelayMs = 250;

    /// <summary>
    /// How long the selection stands on a RAW before its full-size JPEG is
    /// decoded - see <see cref="LoadFullSizeAsync"/>. Longer than a key
    /// repeat, shorter than the hand's way from the arrow keys to the mouse.
    /// </summary>
    private const int FullSizeDwellMs = 150;


    private readonly IImageMetadataReader? _metadataReader;
    private readonly CompanionMetadataService? _companionMetadata;
    private readonly PathClaims? _claims;
    private readonly OperationTracker? _tracker;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private bool _isVisible;
    private FileSystemEntry? _primary;
    private IReadOnlyList<FileSystemEntry> _selection = Array.Empty<FileSystemEntry>();
    private GalleryPalette _contentPalette = GalleryPalette.Plain;
    private string? _currentFolderPath;
    private string _currentFolderName = "";
    private string _folderHeadline = "";
    private string _folderTitle = "";
    private string _folderNote = "";
    private string _archiveHeadline = "";
    private string _archiveMore = "";
    private bool _archiveEntryTooBig;

    // One archive entry is unpacked for a preview at a time - see
    // ArchiveEntryCopyAsync for why the queue is kept this short.
    private readonly SemaphoreSlim _archiveCopyGate = new(1, 1);

    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _summaryCts;

    // What Release let go of, until the next load: the file Restore shows
    // again when the operation left it where it was.
    private string? _releasedPath;

    // The file whose picture is on screen, which between two photographs is
    // not the file selected - see FooterWaitsForPicture. And whether the EXIF
    // kept with that picture is still the old file's.
    private string? _pictureOf;
    private bool _pictureFactsStale;

    // The file the footer's summary is about; null when it is about several,
    // a folder or nothing - see FooterWaitsForPicture.
    private string? _summaryOf;

    private PreviewKind _kind = PreviewKind.None;
    private bool _isLoading;
    private bool _isCensusLoading;
    private string? _text;
    private ImageSource? _image;
    private ImageSource? _zoomImage;
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
    // Who holds the file shut when nothing could be shown: the names, ""
    // when nobody can be named, null when the file is not held.
    private string? _lockedBy;
    private VolumeInfo? _volume;
    private string _workLine = "";
    private int _workLinePending;

    private CancellationTokenSource? _companionCts;
    // The file the rating row and the GUID were filled for. Between two
    // files they stay up until the next file's sidecar is read (see
    // UpdateCompanionsAsync), and a click there must not land on the next
    // file with the previous one's stars.
    private string? _companionsOf;
    private string _summaryNote = "";
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


    /// <param name="claims">What operations of the user's are working on - the footer's "running / queued" line; null shows none.</param>
    /// <param name="tracker">Which of them is on which file right now.</param>
    public PreviewController(
        IImageMetadataReader? metadataReader,
        CompanionMetadataService? companionMetadata,
        PathClaims? claims = null,
        OperationTracker? tracker = null) {

        _metadataReader = metadataReader;
        _companionMetadata = companionMetadata;
        _claims = claims;
        _tracker = tracker;
        if (claims is not null) {
            claims.Changed += (_, _) => ScheduleWorkLine();
        }
        if (tracker is not null) {
            tracker.Changed += (_, _) => ScheduleWorkLine();
        }

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

    /// <summary>
    /// <see cref="Release"/> let go of the content: whatever the view holds
    /// open by itself - the browser's page - is to be let go of too.
    /// </summary>
    public event EventHandler? ContentReleased;


    // --- Output properties (the pane's DataContext is this object) -----

    /// <summary>
    /// The colours of the surround the content is drawn on - the gallery's
    /// while the gallery is on screen, the plain one otherwise. Handed in
    /// by the host, which owns both the view mode and the setting; the
    /// pane only has to agree with the list beside it.
    /// </summary>
    public GalleryPalette ContentPalette {
        get => _contentPalette;
        private set => SetField(ref _contentPalette, value);
    }

    /// <summary>
    /// Whether the pane draws its footer - the summary, the RAW switch, the
    /// companions and the rating row. Off for the second half of a split:
    /// the split doubles the picture only, and the one footer under both
    /// belongs to the selection as a whole.
    /// </summary>
    public bool ShowFooter { get; init; } = true;

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
        private set {
            if (SetField(ref _image, value)) {
                Raise(nameof(ZoomSource));
            }
        }
    }

    /// <summary>
    /// What the 1:1 zoom draws: the biggest picture there is of the file.
    /// For most that is <see cref="Image"/> itself. A RAW is fitted into
    /// the pane from its quick embedded preview, and the full-size JPEG
    /// that arrives a moment later (<see cref="LoadFullSizeAsync"/>) goes
    /// here and nowhere else: put in place of <see cref="Image"/>, the same
    /// picture resampled from four times the pixels was a visible twitch a
    /// quarter of a second after every arrow key - most of all on portrait
    /// frames, which the pane draws biggest (2026-09-21).
    /// </summary>
    public ImageSource? ZoomSource => _zoomImage ?? _image;

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

    /// <summary>
    /// "Running: copy" while an operation of the user's is on the file
    /// shown, "Queued: ..." while it has it claimed and is busy elsewhere;
    /// empty otherwise (PLAN AF, block 0, step 6).
    /// </summary>
    public string WorkLine {
        get => _workLine;
        private set {
            if (SetField(ref _workLine, value)) {
                Raise(nameof(HasWorkLine));
            }
        }
    }

    public bool HasWorkLine => _workLine.Length > 0;

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
        private set {
            if (SetField(ref _summary, value)) {
                Raise(nameof(SummaryHead));
                Raise(nameof(SummaryRest));
            }
        }
    }

    /// <summary>The first line of <see cref="Summary"/> - the name. The footer draws <see cref="SummaryNote"/> after it.</summary>
    public string SummaryHead => _summary.IndexOf('\n') is >= 0 and int end ? _summary[..end] : _summary;

    /// <summary>The lines under the name, line break included; empty when there are none.</summary>
    public string SummaryRest => _summary.IndexOf('\n') is >= 0 and int end ? _summary[end..] : "";

    /// <summary>
    /// The mention of the file's sidecars beside its name: "(+.xmp)", dim,
    /// and nothing more (<see cref="CompanionLabel"/>). It used to be a line
    /// of its own with their full names, which made the footer of a
    /// photograph with a sidecar a line taller than its neighbour's - and
    /// the picture above it, fitted by height, a line shorter.
    /// </summary>
    public string SummaryNote {
        get => _summaryNote;
        private set => SetField(ref _summaryNote, value);
    }

    // --- Companion ("integrated item") block ---------------------------
    // Everything below describes the sidecars folded into the selected
    // row. It sits under the summary in the preview footer, which is where
    // the answer to "what is this file's GUID / how did I rate this shot"
    // belongs: next to the file, not behind a dialog.

    /// <summary>Unity asset GUID, or null when the selection has no <c>.meta</c>.</summary>
    public string? UnityGuid {
        get => _unityGuid;
        private set {
            if (SetField(ref _unityGuid, value)) {
                Raise(nameof(HasUnityGuid));
                Raise(nameof(HasUnityMeta));
            }
        }
    }

    public bool HasUnityGuid => !string.IsNullOrEmpty(_unityGuid);

    /// <summary>A <c>.meta</c> with something to show - the only sidecar that gets rows of its own in the footer.</summary>
    public bool HasUnityMeta => HasUnityGuid || !string.IsNullOrEmpty(_unityDetail);

    /// <summary>Importer name / "folder asset" — context for the GUID above.</summary>
    public string? UnityDetail {
        get => _unityDetail;
        private set {
            if (SetField(ref _unityDetail, value)) {
                Raise(nameof(HasUnityMeta));
            }
        }
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

    /// <summary>
    /// How many other files a star or a swatch would write to besides the
    /// one on screen - the rest of a multi-selection the file is part of.
    /// Zero for a single file, and for a file shown outside its selection.
    /// </summary>
    public int RatingOthersCount =>
        _primary is null || _selection.Count < 2
            || !_selection.Any(e => string.Equals(e.FullPath, _primary.FullPath, StringComparison.OrdinalIgnoreCase))
            ? 0
            : _selection.Count - 1;

    public bool HasRatingOthers => RatingOthersCount > 0;

    /// <summary>"и ещё 4" beside the stars, when the click is about more than the picture shown.</summary>
    public string RatingOthers => HasRatingOthers ? string.Format(Strings.PreviewRatingOthers, RatingOthersCount) : "";

    public string RatingOthersHint => HasRatingOthers ? string.Format(Strings.PreviewRatingOthersHint, RatingOthersCount) : "";

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
        // An entry too big to unpack for a look. Everything else inside an
        // archive is previewed off its scratch copy, and a format the pane
        // cannot read says so in the ordinary words.
        : _archiveEntryTooBig ? Strings.PreviewArchiveTooBig
        : _lockedBy is { Length: > 0 } holders ? string.Format(Strings.PreviewFileLockedBy, holders)
        : _lockedBy is not null ? Strings.PreviewFileLocked
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


    // --- Archive listing -------------------------------------------------
    // An archive file selected in an ordinary folder. The census above
    // cannot describe it - it walks the filesystem, and what is inside an
    // archive is not on one - so the first level is listed instead, which
    // is the question "what is in this zip" asked plainly.

    /// <summary>Counts and the archive file's own size, over the listing.</summary>
    public string ArchiveHeadline {
        get => _archiveHeadline;
        private set => SetField(ref _archiveHeadline, value);
    }

    /// <summary>"and N more" when the listing was cut at the ceiling; blank otherwise.</summary>
    public string ArchiveMore {
        get => _archiveMore;
        private set {
            if (SetField(ref _archiveMore, value)) {
                Raise(nameof(HasArchiveMore));
            }
        }
    }

    public bool HasArchiveMore => _archiveMore.Length > 0;

    /// <summary>First level of the archive, folders first.</summary>
    public ObservableCollection<ArchiveEntryRow> ArchiveEntries { get; } = new();


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

            return string.Join(SummaryText.Gap, parts);
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
        RaiseRatingOthers();
        UpdateWorkLine();
        if (sameFile) {
            ScheduleCompanionUpdate();

            return;
        }

        SchedulePreviewUpdate();
        ScheduleSummaryUpdate();
        ScheduleCompanionUpdate();
    }

    /// <summary>The surround's colours changed - see <see cref="ContentPalette"/>.</summary>
    public void SetPalette(GalleryPalette palette) {
        ContentPalette = palette;
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
        RaiseRatingOthers();
        ScheduleSummaryUpdate();
    }

    private void RaiseRatingOthers() {
        Raise(nameof(RatingOthersCount));
        Raise(nameof(HasRatingOthers));
        Raise(nameof(RatingOthers));
        Raise(nameof(RatingOthersHint));
    }

    public void SetCurrentFolder(string? path, string name) {
        if (_currentFolderPath == path && _currentFolderName == name) {
            return;
        }
        _currentFolderPath = path;
        _currentFolderName = name;
        ScheduleSummaryUpdate();
    }

    /// <summary>
    /// Lets go of the file on show when it is one of <paramref name="paths"/>
    /// or inside one - before a delete, a move or a rename takes them away
    /// (PLAN AF, block 0, step 5). Nothing else makes the pane let go but a
    /// new selection, and a video playing or a PDF on screen holds its file
    /// for as long as it is shown. Playback stops without a word. UI thread,
    /// before the operation starts.
    /// </summary>
    public void Release(IReadOnlyList<string> paths) {
        if (_primary?.FullPath is not { } shown || !paths.Any(p => IsSameOrInside(shown, p))) {
            return;
        }

        _previewCts?.Cancel();
        ClearPreviewContent();
        IsLoading = false;
        // A footer held for a picture whose load was just cancelled
        // (FooterWaitsForPicture) has nothing left to wait for.
        ScheduleSummaryUpdate();
        _releasedPath = shown;
        ContentReleased?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The operation is over: what <see cref="Release"/> let go of is shown
    /// again when it is still there and still what the pane is on - the
    /// operation failed or left it alone. Anything else is the new
    /// selection's to show.
    /// </summary>
    public void Restore() {
        if (_releasedPath is not { } released) {
            return;
        }
        // Another operation still at work on it - a long move of the video
        // on show, while a rename that started earlier ends. Opened again
        // now, the move's last step fails "in use" and leaves a duplicate.
        // That operation's own end brings it back.
        if (_claims?.Covering(released).Any(c => c.Kind == ClaimKind.UserOperation) == true) {
            return;
        }

        _releasedPath = null;
        if (string.Equals(_primary?.FullPath, released, StringComparison.OrdinalIgnoreCase)
            && (File.Exists(released) || Directory.Exists(released))) {
            SchedulePreviewUpdate();
        }
    }


    // --- Preview content pipeline --------------------------------------

    private void SchedulePreviewUpdate() {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        _releasedPath = null;
        _ = UpdatePreviewAsync(_previewCts.Token);
    }

    /// <summary>
    /// The claims or the operations changed, on whatever thread: the line is
    /// worked out again on this one, once for a burst - the tracker alone
    /// reports ten times a second while a copy runs.
    /// </summary>
    private void ScheduleWorkLine() {
        if (Interlocked.Exchange(ref _workLinePending, 1) == 0) {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => {
                Interlocked.Exchange(ref _workLinePending, 0);
                UpdateWorkLine();
            });
        }
    }

    private void UpdateWorkLine() {
        if (_claims is null || _primary?.FullPath is not { } path
            || _claims.Covering(path).FirstOrDefault(c => c.Kind == ClaimKind.UserOperation) is not { } claim) {
            WorkLine = "";

            return;
        }

        bool running = _tracker?.Snapshot().Any(op =>
            op.Verb == claim.Owner && op.CurrentPath is { } current && IsSameOrInside(path, current)) == true;
        WorkLine = string.Format(running ? Strings.PreviewWorkRunning : Strings.PreviewWorkQueued, Strings.Get(claim.Owner));
    }

    private static bool IsSameOrInside(string path, string root) {
        string trimmed = Path.TrimEndingDirectorySeparator(root);

        return string.Equals(path, trimmed, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task UpdatePreviewAsync(CancellationToken ct) {
        // Picture to picture, the one on screen stays until the next is
        // decoded: blanking the pane for the tenth of a second between two
        // photographs is a flash on every arrow key. It goes when the load
        // ends on anything else (below). What the footer says about it
        // stays with it (FooterWaitsForPicture): a footer that loses its
        // EXIF lines for that tenth of a second and gets them back changes
        // height twice, and a portrait picture - fitted by height - was
        // seen to twitch with it (2026-09-21).
        bool pictureToPicture = _isVisible && _kind == PreviewKind.Image
            && _primary is { Kind: EntryKind.File } next
            && PreviewRouter.Route(next.FullPath) is PreviewRoute.Image;
        string? loadingFor = _primary?.FullPath;
        ClearPreviewContent(keepImage: pictureToPicture);
        _pictureFactsStale = pictureToPicture;

        if (!_isVisible) {
            Kind = PreviewKind.None;
            IsLoading = false;
            return;
        }

        // A folder — or an empty selection with a folder open — gets the
        // census instead of "Select a file to preview". Recycled folders do
        // not: their backing path under $Recycle.Bin is not reliably
        // walkable, and the footer already says where they came from.
        // Inside an archive the census has nothing to walk - there is no
        // filesystem in there - but the shell can list the folder, and the
        // pane shows that listing instead: the same one an archive file
        // gets when it is selected from outside, and the answer to "where
        // am I" on a click into empty space.
        if (_primary is null || _primary.Kind != EntryKind.File) {
            string? folder = _primary?.Kind == EntryKind.Directory && _primary.OriginalLocation is null
                ? _primary.FullPath
                : _primary is null ? _currentFolderPath : null;
            if (folder is not null && Archives.Contains(folder)) {
                IsLoading = true;
                try {
                    await LoadArchiveAsync(folder, ct);
                } catch (OperationCanceledException) {
                    // A newer selection took over; it will draw its own.
                } finally {
                    // Not when overtaken: the veil is the newer load's by
                    // then, and it lowers it itself.
                    if (!ct.IsCancellationRequested) {
                        IsLoading = false;
                    }
                }

                return;
            }
            if (folder is not null) {
                // A slow load this selection overtook never lowers its
                // veil (it was cancelled), and the census has a spinner of
                // its own.
                IsLoading = false;
                await ShowFolderCensusAsync(folder, ct);
                return;
            }

            Kind = PreviewKind.None;
            IsLoading = false;
            return;
        }

        bool finished = false;
        _ = RaiseVeilWhenSlowAsync();
        try {
            string path = _primary.FullPath;

            // A file inside an archive has no bytes anyone but the shell
            // can read, so a copy is unpacked into scratch space and the
            // copy goes through the ordinary pipeline. Past the ceiling
            // there is no copy: unpacking half a gigabyte because a row was
            // clicked is not a preview, and "Open" is the way to it.
            if (Archives.Inside(path)) {
                if (_primary.Size > MaxArchivePreviewBytes) {
                    _archiveEntryTooBig = true;
                    Kind = PreviewKind.Unsupported;
                    Raise(nameof(PlaceholderText));

                    return;
                }

                if (await ArchiveEntryCopyAsync(_primary, ct) is not { } copy) {
                    Kind = PreviewKind.Unsupported;

                    return;
                }

                await LoadFileAsync(copy, ct);

                return;
            }

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
                    Raise(nameof(PlaceholderText));
                    return;
                }

                path = resolved;
            }

            await LoadFileAsync(path, ct);
            if (_kind == PreviewKind.Unsupported) {
                await ExplainUnreadableAsync(path, ct);
            }
        } catch (OperationCanceledException) {
            // newer selection won — ignore
        } finally {
            finished = true;
            if (!ct.IsCancellationRequested) {
                IsLoading = false;
                if (_kind != PreviewKind.Image) {
                    // The picture kept across the load, which turned out
                    // not to be one.
                    _zoomImage = null;
                    Image = null;
                }
                if (_pictureFactsStale) {
                    // Nor did the load get as far as reading the new
                    // file's EXIF over the kept one's.
                    _pictureFactsStale = false;
                    ImageMetadata = null;
                    IsRawImage = false;
                }
                _pictureOf = _kind == PreviewKind.Image ? loadingFor : null;
                ScheduleSummaryUpdate();  // metadata might have arrived
            }
        }

        // The veil only for a load worth announcing - see VeilDelayMs. The
        // picture kept on screen (above) is what the wait is spent looking at.
        async Task RaiseVeilWhenSlowAsync() {
            await Task.Delay(VeilDelayMs);
            if (!finished && !ct.IsCancellationRequested) {
                IsLoading = true;
            }
        }
    }

    /// <summary>
    /// A file no loader could show may simply be one another program holds
    /// shut - then the pane says who, instead of "no preview for this
    /// file", which blames the file. Asked only after a load came back
    /// empty: the check opens the file, and the answer names processes.
    /// </summary>
    private async Task ExplainUnreadableAsync(string path, CancellationToken ct) {
        string? holders = await Task.Run(() => {
            if (!IsHeldShut(path)) {
                return null;
            }

            var lockers = ServiceLocator.TryGet<IFileLockInspector>()?.WhoIsLocking(path);

            return lockers is null ? "" : FileLockInfo.Describe(lockers);
        }, ct);
        if (ct.IsCancellationRequested || holders is null) {
            return;
        }

        _lockedBy = holders;
        Raise(nameof(PlaceholderText));
    }

    /// <summary>The file exists and cannot even be opened for reading: somebody holds it without sharing.</summary>
    private static bool IsHeldShut(string path) {
        try {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return false;
        } catch (IOException ex) {
            return FileInUse.Is(ex);
        } catch (UnauthorizedAccessException) {
            return false;
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

        // Whether a file opens as a folder is the shell's answer and costs
        // a call into it, so the cheap half - is this an archive extension
        // on this machine at all - is asked first and settles it for every
        // ordinary file without leaving this thread.
        bool isArchive = Archives.Of(path) is { IsRoot: true }
            && await Task.Run(() => CanNavigate(path), ct);

        switch (PreviewRouter.Route(path, isArchive)) {
            case PreviewRoute.Archive:
                await LoadArchiveAsync(path, ct);
                break;

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
                if (await Task.Run(() => LooksLikeTextAsync(path, ct), ct)) {
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
    /// The scratch copy of one archive entry, unpacked on demand, or null
    /// when the shell would not give it up (a password, a broken archive).
    ///
    /// <para>
    /// A copy already on disk with the entry's size is that entry: the
    /// scratch folder is keyed by the path, and walking a list back and
    /// forth with the arrow keys must not unpack the same file every time
    /// it comes round. Size, because an archive rebuilt under the same name
    /// is a different file - the copy is then made again.
    /// </para>
    ///
    /// <para>
    /// One extraction at a time. The engine cannot be stopped inside an
    /// entry, so a cancelled request keeps its pool thread until the entry
    /// is out; arrow keys over a RAR of scanned pages queued dozens of
    /// those, the pool ran out of threads, and everything else that needed
    /// one - the shell asked whether a file opens as a folder, the listing
    /// of the next archive - waited behind them (session of 2026-09-04,
    /// 85 threads). Waiting at the gate costs nothing: a request cancelled
    /// while it waits leaves before it starts.
    /// </para>
    /// </summary>
    private async Task<string?> ArchiveEntryCopyAsync(FileSystemEntry entry, CancellationToken ct) {
        // A copy is reused when its size matches the entry, and also when
        // the listing gave no size to match against - RAR entries through
        // the shell come without one, and unpacking them again on every
        // pass is what starved the pool. The copy is a day old at most.
        string expected = TempExtraction.CopyPathFor(entry.FullPath);
        long onDisk = SizeOnDisk(expected);
        if (onDisk >= 0 && (entry.Size is null || entry.Size == onDisk)) {
            return expected;
        }

        if (ServiceLocator.TryGet<IShellNamespace>() is not { } ns) {
            return null;
        }

        var log = ServiceLocator.Get<ILogger>();
        try {
            // The queue and the unpacking watched as one wait: what the
            // person sees is a spinner, and which half of it is slow is
            // the second question.
            return await LongWait.WatchAsync(
                UnpackThroughGateAsync(ns, log, entry.FullPath, ct), log, $"preview: unpacking {entry.FullPath}");
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            log.Info($"Preview: cannot unpack {entry.FullPath} - {ex.Message}");

            return null;
        }
    }

    private async Task<string> UnpackThroughGateAsync(IShellNamespace ns, ILogger log, string source, CancellationToken ct) {
        await _archiveCopyGate.WaitAsync(ct);
        try {
            return await TempExtraction.CopyOutAsync(ns, ServiceLocator.Get<IFileSystem>(), log, source, ct);
        } finally {
            _archiveCopyGate.Release();
        }
    }

    /// <summary>The file's length, or -1 when there is no file there.</summary>
    private static long SizeOnDisk(string path) {
        try {
            var info = new FileInfo(path);

            return info.Exists ? info.Length : -1;
        } catch {
            return -1;
        }
    }


    /// <summary>
    /// The first level of an archive: what is in it, without opening it as
    /// a folder. Names and sizes only - no thumbnails and no walk into the
    /// subfolders, both of which mean unpacking, and the pane is for
    /// looking rather than for work (PLAN, decision 4 of section P).
    /// </summary>
    private async Task LoadArchiveAsync(string path, CancellationToken ct) {
        FolderTitle = Path.GetFileName(path);
        ArchiveHeadline = "";
        ArchiveMore = "";
        ArchiveEntries.Clear();
        Kind = PreviewKind.Archive;

        var listing = await LongWait.WatchAsync(
            Task.Run(() => ReadArchive(path, ct), ct), ServiceLocator.Get<ILogger>(), $"preview: listing {path}");
        if (ct.IsCancellationRequested) {
            return;
        }

        if (listing is null) {
            ArchiveHeadline = Strings.PreviewArchiveBroken;

            return;
        }

        foreach (var row in listing.Rows) {
            ArchiveEntries.Add(row);
        }
        ArchiveMore = listing.Hidden > 0
            ? string.Format(Strings.PreviewArchiveMore, listing.Hidden)
            : "";
        // The archive itself, or a folder inside it: only the first has a
        // size of its own to report, and only the first can be "empty or
        // encrypted" - an archive whose entries are encrypted lists as
        // nothing at all, and is not told apart from a genuinely empty one
        // from here.
        bool isRoot = Archives.Of(path) is { IsRoot: true };
        ArchiveHeadline = listing.Files == 0 && listing.Folders == 0
            ? (isRoot ? Strings.PreviewArchiveEmpty : Strings.PreviewFolderEmpty)
            : isRoot
                ? string.Format(
                    Strings.PreviewArchiveHeadline,
                    listing.Files, listing.Folders, SizeFormatter.Format(SizeOf(path)))
                : string.Format(Strings.PreviewArchiveFolderHeadline, listing.Files, listing.Folders);
    }

    /// <summary>
    /// Off the UI thread: the listing, its counts, and an icon per row.
    /// Null when the archive could not be opened at all - a broken file, a
    /// disk that went away.
    /// </summary>
    private static ArchiveListing? ReadArchive(string path, CancellationToken ct) {
        if (ServiceLocator.TryGet<IShellNamespace>() is not { } ns) {
            return null;
        }

        IReadOnlyList<FileSystemEntry> entries;
        try {
            entries = ns.Enumerate(path);
        } catch (Exception) {
            return null;
        }

        int files = entries.Count(e => e.Kind == EntryKind.File);
        var ordered = entries
            .OrderBy(e => e.Kind == EntryKind.Directory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var rows = new List<ArchiveEntryRow>(Math.Min(ordered.Count, ArchiveRowLimit));
        foreach (var entry in ordered.Take(ArchiveRowLimit)) {
            ct.ThrowIfCancellationRequested();
            rows.Add(new ArchiveEntryRow(
                LoadIcon(entry.FullPath),
                entry.Name,
                entry.Size is { } size ? SizeFormatter.Format(size) : ""));
        }

        return new ArchiveListing(rows, files, entries.Count - files, ordered.Count - rows.Count);
    }

    /// <summary>
    /// The icon for one row, built frozen so it can be made here rather
    /// than back on the UI thread. A failure is an icon that stays blank -
    /// the name is what the row is for.
    /// </summary>
    private static ImageSource? LoadIcon(string path) {
        try {
            byte[]? bytes = ServiceLocator.Get<IIconProvider>().GetIcon(path, IconSize.Small);

            return bytes is null ? null : IconConverter.ToImage(bytes);
        } catch {
            return null;
        }
    }

    private static bool CanNavigate(string path) {
        try {
            return ServiceLocator.TryGet<IShellNamespace>()?.CanNavigate(path) == true;
        } catch {
            return false;
        }
    }

    private static long SizeOf(string path) {
        try {
            return new FileInfo(path).Length;
        } catch {
            return 0;
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
            using var file = SharedRead.Open(path);
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
        // The embedded JPEG the picture was decoded from, when it was: the
        // measure of whether the file carries a bigger one.
        int quickBytes = 0;

        await Task.Run(() => {
            ct.ThrowIfCancellationRequested();
            if (_metadataReader is not null) {
                meta = _metadataReader.Read(path);
            }

            // Nothing WIC decodes here turns the picture by itself: the RAW
            // decode and the embedded preview ignore the container's tag,
            // and BitmapImage leaves a JPEG the way the sensor stored it. The
            // camera records the turn in EXIF and every viewer applies it -
            // Explorer, Photos, a browser - so a portrait JPEG shown as it is
            // lies on its side here and nowhere else (found 2026-09-16 on a
            // preview taken out of a RAW as it is, tag and all). The tag is
            // applied to every picture; a file without one is unchanged.
            if (ImageFormats.IsRaw(path)) {
                isRaw = true;
                // The quick embedded preview first - ten milliseconds, and
                // the pane has the picture. The full-size one follows below.
                BitmapImage? raw = null;
                if (!_showRawDecode && ImageDecoder.RawPreviewBytes(path, fullSize: false) is { } jpeg) {
                    raw = ImageDecoder.Stream(jpeg);
                    quickBytes = raw is null ? 0 : jpeg.Length;
                }
                raw ??= ImageDecoder.File(path);
                image = raw is null ? null : ImageDecoder.ApplyOrientation(raw, meta?.Orientation);

                return;
            }

            var plain = ImageDecoder.File(path);
            image = plain is null ? null : ImageDecoder.ApplyOrientation(plain, meta?.Orientation);
        }, ct);

        if (ct.IsCancellationRequested) {
            return;
        }

        _pictureFactsStale = false;
        ImageMetadata = meta;
        IsRawImage = isRaw;
        if (image is not null) {
            _zoomImage = null;
            Image = image;
            Kind = PreviewKind.Image;
            // Only a CR3 carries a bigger JPEG than its quick one: a
            // TIFF-shaped RAW already gave its biggest, and asking again
            // read those megabytes a second time to find the same length.
            if (quickBytes > 0 && Path.GetExtension(path).Equals(".cr3", StringComparison.OrdinalIgnoreCase)) {
                _ = LoadFullSizeAsync(path, meta?.Orientation, image, quickBytes, ct);
            }
        } else {
            Kind = PreviewKind.Unsupported;
        }
    }


    /// <summary>
    /// The second half of a RAW: the biggest JPEG the file carries, for the
    /// 1:1 zoom (<see cref="ZoomSource"/>) - a CR3's quick preview is
    /// 1620 px of a 6000-px frame. The picture fitted into the pane stays
    /// the quick one.
    ///
    /// <para>
    /// Not awaited by the load - the pane is loaded once the quick one is
    /// up - and only after the selection has stood still for a moment: a
    /// held arrow key must not start a 24-megapixel decode, a hundred
    /// megabytes of bitmap, for every frame it passes. A decode cannot be
    /// stopped half-way, only not started.
    /// </para>
    /// </summary>
    private async Task LoadFullSizeAsync(
        string path, int? orientation, BitmapSource quick, int quickBytes, CancellationToken ct) {
        try {
            await Task.Delay(FullSizeDwellMs, ct);

            var full = await Task.Run(() => {
                ct.ThrowIfCancellationRequested();
                // The same bytes again: the file carries one JPEG, and it
                // is already on screen.
                if (ImageDecoder.RawPreviewBytes(path, fullSize: true) is not { } jpeg || jpeg.Length == quickBytes) {
                    return null;
                }

                return ImageDecoder.Stream(jpeg) is { } raw ? ImageDecoder.ApplyOrientation(raw, orientation) : null;
            }, ct);

            if (full is not null && !ct.IsCancellationRequested && ReferenceEquals(_image, quick)) {
                _zoomImage = full;
                Raise(nameof(ZoomSource));
            }
        } catch (OperationCanceledException) {
            // The selection moved on.
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
            // Opening the file is synchronous however the stream is read
            // afterwards; on the pool so a sleeping disk waits there.
            read = await Task.Run(() => PreviewText.ReadAsync(path, ct), ct);
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
            size = await Task.Run(() => new FileInfo(path).Length);
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
                using var file = SharedRead.Open(path);

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



    /// <param name="keepImage">Leave the picture up - the next thing to show is a picture too (<see cref="UpdatePreviewAsync"/>).</param>
    private void ClearPreviewContent(bool keepImage = false) {
        Text = null;
        if (!keepImage) {
            _zoomImage = null;
            Image = null;
            ImageMetadata = null;
            IsRawImage = false;
            _pictureOf = null;
        }
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
        LinkTarget = null;
        _linkBroken = false;
        _archiveEntryTooBig = false;
        _lockedBy = null;
        // Two files in a row can land on Unsupported for different reasons
        // (a broken shortcut, an entry too big to unpack), and the Kind
        // setter then never fires. The placeholder is re-read here, when
        // the reasons are cleared, and again wherever one is set.
        Raise(nameof(PlaceholderText));
        ArchiveEntries.Clear();
        ArchiveHeadline = "";
        ArchiveMore = "";
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
        if (!_isVisible || _companionMetadata is null || _primary is null) {
            ClearCompanionInfo();

            return;
        }

        var companions = _primary.Companions;
        if (companions is null || companions.Count == 0) {
            ClearCompanionInfo();
            // Nothing beside the file — but if it is a photograph, the stars
            // still appear, because "rate this raw" should not mean "go and
            // make it a sidecar first in another program".
            OfferRating(_primary);
            _companionsOf = _primary.FullPath;

            return;
        }

        // Sidecars are tiny, but they still live on the same disk that can
        // be a sleeping spindle or a network share — off the UI thread like
        // every other read here.
        var loaded = await Task.Run(() => Load(companions), ct);
        if (ct.IsCancellationRequested) {
            return;
        }

        // Cleared here, in the same breath as it is filled, not before the
        // read: a block that folds for the few milliseconds of the read and
        // unfolds again changes the footer's height twice per file, and the
        // picture above it - a portrait one is fitted by height - twitches
        // on every arrow key through a folder of photographs with sidecars.
        ClearCompanionInfo();
        UnityGuid = loaded.Meta?.Guid;
        UnityDetail = DescribeMeta(loaded.Meta);
        ShowRating(loaded.RatingPath, loaded.Rating);

        // A photo can have companions and still no place for a rating — a
        // Unity .meta next to a PNG is the everyday case.
        if (loaded.RatingPath is null) {
            OfferRating(_primary);
        }
        _companionsOf = _primary.FullPath;
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
    /// the sidecar formats Wander writes are photo formats. And only where
    /// a sidecar can be written: next to a <c>$R</c> file in the Recycle
    /// Bin it is litter, inside an archive it is impossible.
    /// </summary>
    private void OfferRating(FileSystemEntry entry) {
        if (RatingRequested is null || entry.IsFolderLike || !ImageFormats.IsImage(entry.Name)) {
            return;
        }
        if (entry.OriginalLocation is not null || Archives.Of(entry.FullPath) is not null) {
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
            ? rating.ColorLabelName
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

        return parts.Count > 0 ? string.Join(SummaryText.Gap, parts) : null;
    }

    private void SetRating(RatingField field, object? parameter, int current) {
        if (RatingRequested is not { } write || _primary is null || !TryReadIndex(parameter, out int clicked)) {
            return;
        }
        // The stars on show are still the previous file's: its sidecar is
        // being read. "current" would be theirs, and a click meant as "set
        // three" could clear this file's rating instead.
        if (!IsCompanionBlockFor(_primary)) {
            return;
        }

        // Whether the click sets or clears is the host's call: clicking
        // what is already set clears it, but "already set" is a question
        // about every file the click is for, and only the host knows how
        // many that is. The pane hands over what it shows and lets go.
        var request = new RatingRequestedEventArgs(_primary, field, clicked, current);
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
        if (string.IsNullOrEmpty(_unityGuid) || _primary is null || !IsCompanionBlockFor(_primary)) {
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

    private bool IsCompanionBlockFor(FileSystemEntry entry) {
        return string.Equals(_companionsOf, entry.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearCompanionInfo() {
        _companionsOf = null;
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


    /// <summary>What one read of an archive came back with.</summary>
    /// <param name="Hidden">Entries past <see cref="ArchiveRowLimit"/>.</param>
    private sealed record ArchiveListing(
        IReadOnlyList<ArchiveEntryRow> Rows, int Files, int Folders, int Hidden);


    private void ScheduleSummaryUpdate() {
        _summaryCts?.Cancel();
        _summaryCts = new CancellationTokenSource();
        _ = UpdateSummaryAsync(_summaryCts.Token);
    }

    /// <summary>
    /// Between two photographs the picture on screen is still the previous
    /// file's (<see cref="UpdatePreviewAsync"/>), and the footer stays with
    /// it: the name, the size and the EXIF change once, together with the
    /// picture, instead of the EXIF going and coming back under it. The
    /// load's end schedules the summary again, whatever it ended on.
    ///
    /// <para>
    /// Only while the footer is about that picture: coming from several
    /// files selected, it said "3 selected" and would go on saying it under
    /// the next photograph until it decoded.
    /// </para>
    /// </summary>
    private bool FooterWaitsForPicture(FileSystemEntry selected) {
        return _image is not null && _pictureOf is { } shown
            && string.Equals(_summaryOf, shown, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(shown, selected.FullPath, StringComparison.OrdinalIgnoreCase)
            && PreviewRouter.Route(selected.FullPath) is PreviewRoute.Image;
    }

    private async Task UpdateSummaryAsync(CancellationToken ct) {
        if (!_isVisible) {
            Summary = "";
            SummaryNote = "";
            _summaryOf = null;

            return;
        }

        // 1. Single file selected — its details, plus EXIF if the metadata
        //    reader had something to say about it. EXIF still kept with
        //    another file's picture is not this file's (a footer not held
        //    for it, see FooterWaitsForPicture) - the load's end brings its own.
        if (_selection.Count == 1 && _selection[0].Kind == EntryKind.File) {
            if (!FooterWaitsForPicture(_selection[0])) {
                bool othersFacts = _pictureFactsStale
                    && !string.Equals(_pictureOf, _selection[0].FullPath, StringComparison.OrdinalIgnoreCase);
                Summary = SummaryText.ForFile(_selection[0], othersFacts ? null : _imageMetadata);
                SummaryNote = CompanionLabel.For(_selection[0].Name, _selection[0].Companions);
                _summaryOf = _selection[0].FullPath;
            }

            return;
        }

        SummaryNote = "";
        _summaryOf = null;

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
            // Inside an archive the walk has nothing to walk: the rows are
            // the whole listing there, so they are added up as they stand.
            // Folders inside contribute nothing - the shell reports no size
            // for them, and guessing one would be worse than leaving it out.
            if (Archives.Inside(_selection[0].FullPath)) {
                var files = _selection.Where(en => en.Kind == EntryKind.File).ToList();
                Summary = string.Format(
                    Strings.SummarySelected, _selection.Count, files.Count,
                    SizeFormatter.Format(files.Sum(en => en.Size ?? 0)));

                return;
            }

            Summary = string.Format(Strings.SummarySelectedCounting, _selection.Count);
            var paths = _selection.Select(en => en.FullPath).ToArray();
            // Pictures among the selection: what they have in common goes
            // under the count. Read on the same worker, after the sizes,
            // so a selection of two thousand RAW files still gets its
            // byte count while the headers are being read.
            var pictures = _selection
                .Where(en => en.Kind == EntryKind.File && ImageFormats.IsImage(en.Name))
                .Select(en => en.FullPath)
                .ToArray();
            var (count, size, shots, read) = await Task.Run(() => {
                var totals = SummaryText.CountAndSum(paths, ct);
                var (summary, opened) = SummariseShots(pictures, ct);

                return (totals.Count, totals.Size, summary, opened);
            }, ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            string text = string.Format(
                Strings.SummarySelected, _selection.Count, count, SizeFormatter.Format(size));
            if (shots is not null) {
                text += "\n" + SummaryText.ForShots(shots, read);
            }
            Summary = text;

            return;
        }

        // 4. Nothing selected — the census panel above describes the folder
        //    we are standing in, so the footer only names it.
        Summary = string.IsNullOrEmpty(_currentFolderPath)
            ? ""
            : SummaryText.ForCurrentFolder(_currentFolderPath!, _currentFolderName);
    }

    /// <summary>
    /// Opens the first <see cref="ShotSummarySample"/> pictures for their
    /// EXIF and folds them into one summary. Null when there are no
    /// pictures, or nothing to read them with. A file the reader cannot
    /// make sense of simply does not take part.
    /// </summary>
    private (ShotSummary? Summary, int Read) SummariseShots(string[] pictures, CancellationToken ct) {
        if (pictures.Length == 0 || _metadataReader is null) {
            return (null, 0);
        }

        var shots = new List<ImageMetadata>();
        int read = 0;
        foreach (string path in pictures.Take(ShotSummarySample)) {
            if (ct.IsCancellationRequested) {
                break;
            }
            read++;
            try {
                if (_metadataReader.Read(path) is { } meta) {
                    shots.Add(meta);
                }
            } catch {
                // Unreadable or not a picture after all: no vote.
            }
        }

        var summary = ShotSummary.Aggregate(shots);

        return (summary with { Shots = pictures.Length }, read);
    }
}
