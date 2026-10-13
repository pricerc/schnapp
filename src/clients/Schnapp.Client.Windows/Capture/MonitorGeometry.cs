namespace Schnapp.Client.Windows.Capture;

/// <summary>
/// Per-monitor geometry for capture UI and bounds.
/// </summary>
/// <remarks>
/// On Windows GDI, <see cref="DetectedScale"/> / <see cref="OsScale"/> are always 1 —
/// the user picks discrete downscales in Settings via <see cref="MonitorScaling"/>.
/// </remarks>
public sealed class MonitorGeometry
{
    /// <summary>Zero-based monitor index in the provider's enumeration order.</summary>
    public int Index { get; set; }

    /// <summary>Device name (e.g. <c>\\.\DISPLAY1</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional friendly description when available.</summary>
    public string Description { get; set; } = "";

    /// <summary>Origin X in the virtual desktop coordinate space (device pixels when DPI-aware).</summary>
    public int LogicalX { get; set; }

    /// <summary>Origin Y in the virtual desktop coordinate space.</summary>
    public int LogicalY { get; set; }

    /// <summary>Width in pixels used for capture bounds.</summary>
    public int LogicalWidth { get; set; }

    /// <summary>Height in pixels used for capture bounds.</summary>
    public int LogicalHeight { get; set; }

    /// <summary>Same as logical on Windows GDI path (no separate CRTC).</summary>
    public int PhysicalWidth { get; set; }

    /// <summary>Same as logical on Windows GDI path.</summary>
    public int PhysicalHeight { get; set; }

    /// <summary>Always 1.0 on Windows GDI (no auto DPI shrink).</summary>
    public double OsScale { get; set; } = 1.0;

    /// <summary>Always 1.0 on Windows GDI; output scale is user-selected only.</summary>
    public double DetectedScale { get; set; } = 1.0;

    /// <summary>Base width for 100% output (logical x detected scale).</summary>
    public int RecommendedWidth =>
        System.Math.Max(1, (int)System.Math.Round(LogicalWidth * DetectedScale));

    /// <summary>Base height for 100% output (logical x detected scale).</summary>
    public int RecommendedHeight =>
        System.Math.Max(1, (int)System.Math.Round(LogicalHeight * DetectedScale));
}
