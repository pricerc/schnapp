namespace Schnapp.Client.Windows.Capture;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Platform screen capture. Targets may be combined in one tick.
/// </summary>
/// <remarks>
/// Implemented by <see cref="GdiScreenCapture"/> (day-1). Call sites: <c>CaptureService</c> only.
/// </remarks>
public interface IScreenCapture
{
    /// <summary>True when the backend can capture.</summary>
    bool IsAvailable { get; }

    /// <summary>Number of monitors known to the screen info provider.</summary>
    /// <returns>Count of attached displays (at least 1 on a normal desktop).</returns>
    int GetMonitorCount();

    /// <summary>Human label for a monitor index (for settings UI).</summary>
    /// <param name="monitorIndex">Zero-based monitor index.</param>
    /// <returns>Short label including size when available.</returns>
    string GetMonitorName(int monitorIndex);

    /// <summary>
    /// Auto post-capture scale factor (≤ 1). On Windows GDI this is always <c>1.0</c>;
    /// the user chooses discrete scales in Settings via <see cref="MonitorScaling"/>.
    /// </summary>
    /// <param name="monitorIndex">Zero-based monitor index.</param>
    /// <returns>Detected scale factor (always 1.0 on Windows GDI).</returns>
    double GetMonitorDetectedScale(int monitorIndex);

    /// <summary>Full logical/physical/scale snapshot for one monitor (UI + capture).</summary>
    /// <param name="monitorIndex">Zero-based monitor index.</param>
    /// <returns>Geometry for the monitor.</returns>
    MonitorGeometry GetMonitorGeometry(int monitorIndex);

    /// <summary>
    /// Min auto scale across all monitors (full-desktop). Always <c>1.0</c> on Windows GDI.
    /// </summary>
    /// <returns>Desktop-wide auto scale factor.</returns>
    double GetDesktopAutoScale();

    /// <summary>
    /// Id of the focused top-level window to arm the focus-dwell snapshot.
    /// Null when there is no window, it belongs to this process, it is the shell/desktop, or it is too small.
    /// </summary>
    /// <returns>HWND as a 64-bit id, or null.</returns>
    long? TryGetFocusedWindowId();

    /// <summary>
    /// Captures enabled targets (any combination). Prefer bulk grab so timestamps stay close.
    /// </summary>
    /// <param name="captureWindow">Active/focused window.</param>
    /// <param name="captureMonitors">Per-monitor images.</param>
    /// <param name="captureFullDesktop">Full virtual desktop (stitched).</param>
    /// <param name="monitorIndexes">
    /// When capturing monitors: null/empty = all monitors; otherwise only these zero-based indexes.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One result per successful target; caller disposes each <see cref="ScreenCaptureResult"/>.</returns>
    Task<IReadOnlyList<ScreenCaptureResult>> CaptureAsync(
        bool captureWindow,
        bool captureMonitors,
        bool captureFullDesktop,
        IReadOnlyList<int>? monitorIndexes = null,
        CancellationToken ct = default);
}
