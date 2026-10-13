namespace Schnapp.Client.Linux.Capture;

using Avalonia;

/// <summary>
/// Monitor geometry for per-display region capture and scale detection.
/// </summary>
public interface IScreenInfoProvider
{
    /// <summary>Number of known monitors (at least 1 when a fallback primary is used).</summary>
    int GetMonitorCount();

    /// <summary>Logical bounds of a monitor in root-window coordinates.</summary>
    PixelRect GetMonitorBounds(int monitorIndex);

    /// <summary>Human label for a monitor index (e.g. RandR connector or "Monitor 1").</summary>
    string GetMonitorName(int monitorIndex);

    /// <summary>
    /// Auto X11 scale multiply (≤1) from RandR physical/logical. Default 1.0.
    /// </summary>
    double GetMonitorDetectedScale(int monitorIndex);

    /// <summary>Full logical/physical/scale snapshot for UI and capture.</summary>
    MonitorGeometry GetMonitorGeometry(int monitorIndex);

    /// <summary>
    /// Min auto X11 scale across all monitors (for full-desktop fat-FB shrink).
    /// </summary>
    double GetDesktopAutoScale();

    /// <summary>
    /// Index of the monitor that contains the given root-window point, or null.
    /// </summary>
    int? FindMonitorAt(int rootX, int rootY);
}
