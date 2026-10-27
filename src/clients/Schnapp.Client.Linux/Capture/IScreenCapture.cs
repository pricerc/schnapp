namespace Schnapp.Client.Linux.Capture;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Platform screen capture. Targets may be combined in one tick.
/// </summary>
public interface IScreenCapture
{
    /// <summary>True when the backend can capture (e.g. X11 display open).</summary>
    bool IsAvailable { get; }

    /// <summary>Number of monitors known to the screen info provider.</summary>
    int GetMonitorCount();

    /// <summary>Human label for a monitor index (for settings UI).</summary>
    string GetMonitorName(int monitorIndex);

    /// <summary>
    /// Suggested post-capture scale (fraction of X pixels kept per axis). Default 1.0.
    /// </summary>
    double GetMonitorDetectedScale(int monitorIndex);

    /// <summary>Full logical/physical/scale snapshot for one monitor (UI + capture).</summary>
    MonitorGeometry GetMonitorGeometry(int monitorIndex);

    /// <summary>
    /// Min auto scale across all monitors (for full-desktop fat-framebuffer shrink). Default 1.0.
    /// </summary>
    double GetDesktopAutoScale();

    /// <summary>
    /// Id of the focused top-level window to arm the focus-dwell snapshot.
    /// Null when there is no window, it is this process, the root/desktop, or too small to be a real window.
    /// </summary>
    long? TryGetFocusedWindowId();

    /// <summary>
    /// Captures enabled targets (any combination). Prefer bulk/raw-then-process so timestamps stay close.
    /// </summary>
    /// <param name="captureWindow">Active/focused window.</param>
    /// <param name="captureMonitors">Per-monitor images.</param>
    /// <param name="captureFullDesktop">Full root desktop (stitched).</param>
    /// <param name="monitorIndexes">
    /// When capturing monitors: null/empty = all monitors; otherwise only these zero-based indexes.
    /// </param>
    Task<IReadOnlyList<ScreenCaptureResult>> CaptureAsync(
        bool captureWindow,
        bool captureMonitors,
        bool captureFullDesktop,
        IReadOnlyList<int>? monitorIndexes = null,
        CancellationToken ct = default);
}
