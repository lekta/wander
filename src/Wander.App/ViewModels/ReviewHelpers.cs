namespace Wander.App.ViewModels;

/// <summary>
/// Which review helpers are switched on (RAWHELPERS): marks and numbers
/// over a photograph while picking the keepers. One instance for the
/// window - the buttons in the preview footer edit it, both halves of the
/// pane and the gallery's sharpness pass read it. Not kept between runs
/// [decision: per session].
///
/// <para>
/// Nothing here excludes anything: shadows and highlights work at opposite
/// ends of the range, so both at once is a frame opened up from both sides,
/// which is a fair thing to ask for.
/// </para>
/// </summary>
public sealed class ReviewHelpers : ObservableObject {
    private bool _peaking;
    private bool _sharpness;
    private bool _clipping;
    private bool _histogram;
    private bool _shadows;
    private bool _highlights;
    private bool _afPoints;
    private bool _peek;


    /// <summary>Focus peaking: the crisp edges marked in colour.</summary>
    public bool Peaking {
        get => _peaking;
        set => SetField(ref _peaking, value);
    }

    /// <summary>The sharpness score, 0..100: beside the buttons for the picture shown, on every cell of the gallery.</summary>
    public bool Sharpness {
        get => _sharpness;
        set => SetField(ref _sharpness, value);
    }

    /// <summary>Clipped highlights by channel and crushed shadows, marked in colour.</summary>
    public bool Clipping {
        get => _clipping;
        set => SetField(ref _clipping, value);
    }

    /// <summary>Levels by channel in the footer.</summary>
    public bool Histogram {
        get => _histogram;
        set => SetField(ref _histogram, value);
    }

    /// <summary>The picture shown with its shadows lifted.</summary>
    public bool Shadows {
        get => _shadows;
        set => SetField(ref _shadows, value);
    }

    /// <summary>The picture shown with its highlights opened up. Together with <see cref="Shadows"/> when both are wanted: each works at its own end.</summary>
    public bool Highlights {
        get => _highlights;
        set => SetField(ref _highlights, value);
    }

    /// <summary>The autofocus areas the camera recorded, framed over the picture.</summary>
    public bool AfPoints {
        get => _afPoints;
        set => SetField(ref _afPoints, value);
    }

    /// <summary>
    /// Alt is held: the picture is shown as it is, without marks and
    /// curves, for as long as it is. Not a switch - nothing is worked out
    /// again, only hidden - so it is set through <see cref="SetPeek"/>.
    /// </summary>
    public bool Peek {
        get => _peek;
        private set => SetField(ref _peek, value);
    }

    /// <summary>Anything that needs the picture's pixels is on.</summary>
    public bool AnyOn => _peaking || _sharpness || _clipping || _histogram || _shadows || _highlights || _afPoints;


    public void SetPeek(bool peek) {
        Peek = peek;
    }
}
