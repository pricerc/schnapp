namespace Schnapp.Client.Linux.Capture;

/// <summary>
/// Per-monitor logical/physical geometry and auto X11 scale (see docs/per-monitor-scale-plan.md).
/// </summary>
public sealed class MonitorGeometry
{
    /// <summary>Zero-based monitor index in the provider's enumeration order.</summary>
    public int Index { get; init; }

    /// <summary>RandR connector name (e.g. DisplayPort-1).</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Human monitor description from EDID when available (e.g. "AOC 31\"" or "AOC U32P2WG6").
    /// Empty if unknown.
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>Logical origin X in the root desktop coordinate space.</summary>
    public int LogicalX { get; init; }

    /// <summary>Logical origin Y in the root desktop coordinate space.</summary>
    public int LogicalY { get; init; }

    /// <summary>Logical width in pixels (what Xinerama / capture regions use).</summary>
    public int LogicalWidth { get; init; }

    /// <summary>Logical height in pixels.</summary>
    public int LogicalHeight { get; init; }

    /// <summary>Physical panel width after rotation correction (from RandR preferred mode).</summary>
    public int PhysicalWidth { get; init; }

    /// <summary>Physical panel height after rotation correction.</summary>
    public int PhysicalHeight { get; init; }

    /// <summary>RandR CRTC rotation flags.</summary>
    public int Rotation { get; init; }

    /// <summary>Auto multiply capture → recommended; always ≤ 1.</summary>
    public double X11Scale { get; init; } = 1.0;

    /// <summary>Suggested capture width after applying <see cref="X11Scale"/>.</summary>
    public int RecommendedWidth =>
        System.Math.Max(1, (int)System.Math.Round(LogicalWidth * X11Scale));

    /// <summary>Suggested capture height after applying <see cref="X11Scale"/>.</summary>
    public int RecommendedHeight =>
        System.Math.Max(1, (int)System.Math.Round(LogicalHeight * X11Scale));
}
