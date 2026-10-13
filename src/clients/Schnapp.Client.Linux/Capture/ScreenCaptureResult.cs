namespace Schnapp.Client.Linux.Capture;

using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;

/// <summary>
/// One captured frame as an ImageSharp image (caller disposes), plus upload metadata.
/// </summary>
public sealed class ScreenCaptureResult : IDisposable
{
    /// <summary>
    /// Decoded/constructed pixels ready for resize + encode. Owned by this result.
    /// </summary>
    public required Image Image { get; init; }

    /// <summary>Pixel width of <see cref="Image"/>.</summary>
    public int Width => Image.Width;

    /// <summary>Pixel height of <see cref="Image"/>.</summary>
    public int Height => Image.Height;

    /// <summary>Window, Monitor, or Full.</summary>
    public string CaptureType { get; init; } = "Full";

    /// <summary>Zero-based monitor index when <see cref="CaptureType"/> is Monitor.</summary>
    public int? MonitorIndex { get; init; }

    /// <summary>Human monitor label when known.</summary>
    public string? MonitorName { get; init; }

    /// <summary>Active window title for window captures, if available.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>Active program/class name for window captures, if available.</summary>
    public string? ProgramName { get; init; }

    /// <summary>Short human description used in notes/UI defaults.</summary>
    public string? Description { get; init; }

    /// <summary>Extra string key/values attached by the capture backend.</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>Disposes the owned <see cref="Image"/>.</summary>
    public void Dispose() => Image.Dispose();
}
