namespace Schnapp.Client.Core.Capture;

/// <summary>
/// Decides when a newly focused window has stayed focused long enough for one snapshot.
/// </summary>
/// <remarks>
/// One shot per focus session. Leaving the window and coming back starts a new wait.
/// A capture of the same window within <see cref="SameWindowDedupe"/> counts as that shot
/// so the interval timer and this trigger do not store two frames a few seconds apart.
/// Platform code supplies the window id; <c>null</c> or a non-positive id means "no window"
/// (desktop, this process, or nothing focused) and drops the current session.
/// </remarks>
public sealed class WindowFocusDwellTracker
{
    /// <summary>How often a client should sample the focused window. Dwell is in whole seconds, so this bound is enough.</summary>
    public const int PollMilliseconds = 250;

    /// <summary>
    /// Same window captured inside this span suppresses the other trigger's window frame.
    /// </summary>
    public static readonly TimeSpan SameWindowDedupe = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private long? _windowId;
    private DateTime _focusedSinceUtc;
    private bool _fired;
    private long? _lastCapturedWindowId;
    private DateTime _lastCapturedUtc;

    /// <summary>
    /// Reports the focused window. Returns true once the same id has been focused for at least <paramref name="dwell"/>.
    /// </summary>
    /// <param name="windowId">Stable top-level window id, or null when none should be tracked.</param>
    /// <param name="utcNow">Sample time (UTC).</param>
    /// <param name="dwell">Required focus time. Zero or negative never fires.</param>
    /// <returns>True when a snapshot is due and has not yet been committed for this session.</returns>
    public bool IsDue(long? windowId, DateTime utcNow, TimeSpan dwell)
    {
        lock (_gate)
        {
            if (dwell <= TimeSpan.Zero || windowId is null || windowId.Value <= 0)
            {
                // Because a missing window is not a focus session — drop the clock so the next real window starts clean.
                ClearSession();
                return false;
            }

            if (_windowId != windowId)
            {
                _windowId = windowId;
                _focusedSinceUtc = utcNow;
                _fired = false;
                return false;
            }

            if (_fired)
            {
                return false;
            }

            return utcNow - _focusedSinceUtc >= dwell;
        }
    }

    /// <summary>
    /// Marks the current session done when it is still <paramref name="windowId"/>, and records a recent capture.
    /// </summary>
    /// <param name="windowId">Window that was snapshotted or already covered by another shot.</param>
    /// <param name="utcNow">Capture time (UTC).</param>
    public void Commit(long windowId, DateTime utcNow)
    {
        lock (_gate)
        {
            if (_windowId == windowId)
            {
                _fired = true;
            }

            NoteCapturedUnlocked(windowId, utcNow);
        }
    }

    /// <summary>
    /// Records that a window frame was stored, without ending a dwell session that has not elapsed yet.
    /// </summary>
    /// <param name="windowId">Captured window id.</param>
    /// <param name="utcNow">Capture time (UTC).</param>
    public void NoteCaptured(long windowId, DateTime utcNow)
    {
        lock (_gate)
        {
            NoteCapturedUnlocked(windowId, utcNow);
        }
    }

    /// <summary>
    /// True when <paramref name="windowId"/> was captured within <see cref="SameWindowDedupe"/>.
    /// </summary>
    /// <param name="windowId">Window to test.</param>
    /// <param name="utcNow">Time of the check (UTC).</param>
    public bool WasRecentlyCaptured(long windowId, DateTime utcNow)
    {
        lock (_gate)
        {
            return _lastCapturedWindowId == windowId
                && utcNow - _lastCapturedUtc < SameWindowDedupe;
        }
    }

    /// <summary>
    /// Drops the in-progress focus session. Recent-capture dedupe is kept.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            ClearSession();
        }
    }

    private void ClearSession()
    {
        _windowId = null;
        _fired = false;
    }

    private void NoteCapturedUnlocked(long windowId, DateTime utcNow)
    {
        if (windowId <= 0)
        {
            return;
        }

        _lastCapturedWindowId = windowId;
        _lastCapturedUtc = utcNow;
    }
}
