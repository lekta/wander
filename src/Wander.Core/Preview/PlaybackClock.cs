namespace Wander.Core.Preview;

/// <summary>
/// When a clip has finished, for players that do not say so.
///
/// <para>
/// The media element raises "ended" for well-formed files and stays silent
/// for some others: a clip whose container reports no length (or a length of
/// zero) plays to its last frame and nothing happens - the button keeps
/// showing a pause it cannot honour, repeat never fires, and a second press
/// asks a stopped player to play from a position it is already at. This
/// watches the position instead: a player that claims to be playing while
/// its position stands still has finished.
/// </para>
///
/// <para>
/// Lives in Core because it is a rule with no player in it - positions in,
/// a verdict out - and because "how many still ticks mean the end" is
/// exactly the kind of number that wants a test rather than a guess in a
/// timer handler.
/// </para>
/// </summary>
public sealed class PlaybackClock {
    /// <summary>
    /// Still ticks before the clip counts as finished. Five of the pane's
    /// 200 ms ticks - a whole second of a position that does not move.
    /// Fewer would call the end on a stutter (a cold disk, a share); more
    /// and the button stays wrong long enough to be pressed twice.
    /// </summary>
    private const int StillTicksToEnd = 5;

    /// <summary>
    /// How close to the length counts as being at it. A player reports its
    /// last position a frame or two short of the length it declared, so an
    /// exact comparison never matches.
    /// </summary>
    private const double EndToleranceSeconds = 0.35;

    /// <summary>
    /// Below this a clip is treated as a loop rather than as a film. Three
    /// seconds is the length at which watching something once stops being
    /// enough to see it.
    /// </summary>
    private const double ShortClipSeconds = 3;

    private TimeSpan _last = TimeSpan.MinValue;
    private int _still;


    /// <summary>
    /// Is this position at the end of a clip of this length? What a press
    /// of "play" has to ask before playing: from the end, a player does
    /// nothing at all, so the press has to rewind first.
    /// </summary>
    /// <param name="duration">Null when the player will not say - then only "not at the start" is knowable.</param>
    public static bool AtEnd(TimeSpan position, TimeSpan? duration) {
        if (position <= TimeSpan.Zero) {
            return false;
        }

        return duration is not { } total || total <= TimeSpan.Zero
            ? true
            : position.TotalSeconds >= total.TotalSeconds - EndToleranceSeconds;
    }


    /// <summary>
    /// Should repeat start on for a clip of this length? A length nobody
    /// declared counts as short: the files that do not declare one are the
    /// two-second clips, and the cost of being wrong is a button the user
    /// can switch off.
    /// </summary>
    public static bool LoopsByDefault(TimeSpan? duration) {
        return duration is not { } total
            || total <= TimeSpan.Zero
            || total.TotalSeconds < ShortClipSeconds;
    }


    /// <summary>A new file, a seek, a deliberate stop: nothing is known about the position again.</summary>
    public void Reset() {
        _last = TimeSpan.MinValue;
        _still = 0;
    }


    /// <summary>
    /// One tick of the player's clock. True when the clip has finished
    /// without the player saying so — the caller then does what it would
    /// have done for a real "ended".
    /// </summary>
    public bool NoteTick(TimeSpan position, TimeSpan? duration, bool playing) {
        if (!playing) {
            Reset();

            return false;
        }

        if (position != _last) {
            _last = position;
            _still = 0;

            return false;
        }

        // Standing still. Only a position that has actually got somewhere
        // counts: a player still opening its file sits at zero, and calling
        // that the end would rewind a clip that never started.
        if (position <= TimeSpan.Zero || !AtEnd(position, duration)) {
            return false;
        }

        if (++_still < StillTicksToEnd) {
            return false;
        }

        Reset();

        return true;
    }
}
