namespace Schnapp.Client.Core.Config;

/// <summary>Per-monitor capture overrides (X11 scale override, output size, quality).</summary>
public sealed class MonitorCaptureSettings
{
    /// <summary>
    /// When true (fat FB only), 100% output is CRTC/logical size (skip auto X11 shrink).
    /// When false, 100% is recommended (physical when fat FB).
    /// </summary>
    public bool OverrideX11Scale { get; set; }

    /// <summary>
    /// Screenshot size as percent of base (25-100). Higher = larger.
    /// Base = recommended when auto; CRTC when override.
    /// </summary>
    public double? OutputPercent { get; set; }

    /// <summary>Lossy encode quality 1-100. Null = global default.</summary>
    public int? Quality { get; set; }

    public void Normalize()
    {
        if (OutputPercent is double op)
        {
            OutputPercent = op < 10 ? 10 : (op > 100 ? 100 : op);
        }
    }

    /// <summary>X11 multiply: override or detected auto.</summary>
    public double ResolveX11Factor(double detectedAuto)
    {
        Normalize();
        // Override = use CRTC as 100% base (no X11 shrink). Auto = RandR factor.
        if (OverrideX11Scale)
        {
            return 1.0;
        }

        if (detectedAuto > 0)
        {
            return detectedAuto < 0.25 ? 0.25 : (detectedAuto > 1.0 ? 1.0 : detectedAuto);
        }

        return 1.0;
    }

    public double ResolveOutputFraction()
    {
        Normalize();
        if (OutputPercent is double p && p > 0)
        {
            var f = p / 100.0;
            return f < 0.25 ? 0.25 : (f > 1.0 ? 1.0 : f);
        }
        return 1.0;
    }

    public double ResolveCombinedFactor(double detectedAuto)
    {
        var c = ResolveX11Factor(detectedAuto) * ResolveOutputFraction();
        return c < 0.05 ? 0.05 : (c > 1.0 ? 1.0 : c);
    }
}
