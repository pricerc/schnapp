namespace Schnapp.Client.Windows.Capture;

using System;
using System.Collections.Generic;
using System.Drawing;

/// <summary>
/// One captured frame as a GDI+ <see cref="Bitmap"/> (caller disposes), plus upload metadata.
/// </summary>
public sealed class ScreenCaptureResult : IDisposable
{
    /// <summary>
    /// Decoded/constructed pixels ready for resize + encode. Owned by this result.
    /// </summary>
    public Bitmap Image { get; set; } = null!;

    /// <summary>Pixel width of <see cref="Image"/>.</summary>
    public int Width => Image?.Width ?? 0;

    /// <summary>Pixel height of <see cref="Image"/>.</summary>
    public int Height => Image?.Height ?? 0;

    /// <summary>Window, Monitor, or Full.</summary>
    public string CaptureType { get; set; } = "Full";

    /// <summary>Zero-based monitor index when <see cref="CaptureType"/> is Monitor.</summary>
    public int? MonitorIndex { get; set; }

    /// <summary>Human monitor label when known.</summary>
    public string? MonitorName { get; set; }

    /// <summary>Active window title for window captures, if available.</summary>
    public string? WindowTitle { get; set; }

    /// <summary>Active program/class name for window captures, if available.</summary>
    public string? ProgramName { get; set; }

    /// <summary>Short human description used in notes/UI defaults.</summary>
    public string? Description { get; set; }

    /// <summary>Extra string key/values attached by the capture backend.</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>Disposes the owned <see cref="Image"/>.</summary>
    public void Dispose()
    {
        Image?.Dispose();
        Image = null!;
    }
}
