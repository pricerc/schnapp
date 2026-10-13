namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Schnapp.Client.Core.Config;
using Schnapp.Client.Windows.Capture;

/// <summary>
/// Per-monitor (or full-desktop) output size / quality row for Settings.
/// </summary>
/// <remarks>
/// Scale steps come from <see cref="MonitorScaling"/> (same math as Linux), ordered
/// smallest→largest so the trackbar has ~25% on the left and 100% on the right.
/// WinForms <see cref="TrackBar"/> uses integer indices mapped through
/// <see cref="_scalesByTick"/>. No Xlibre “override OS scale” on Windows;
/// saved <c>OverrideX11Scale</c> is always false. GDI auto scale is always 1.
/// </remarks>
public partial class MonitorSettingsRow : UserControl
{
    /// <summary>Live geometry for this row (monitor dimensions or virtual desktop).</summary>
    private MonitorGeometry _geo = new();

    /// <summary>
    /// When true, slider/checkbox change handlers skip work (programmatic updates).
    /// </summary>
    private bool _suppressEvents;

    /// <summary>
    /// TrackBar Value → discrete <see cref="MonitorScale"/>.
    /// Index 0 = smallest (left); max index = 100% (right). List order matches
    /// <see cref="MonitorScaling.GetMonitorScales"/> (no reverse).
    /// </summary>
    private Dictionary<int, MonitorScale> _scalesByTick = [];

    /// <summary>Designer constructor.</summary>
    public MonitorSettingsRow()
    {
        InitializeComponent();
    }

    /// <summary>Zero-based monitor index; -1 for desktop row.</summary>
    public int MonitorIndex { get; private set; }

    /// <summary>Whether this monitor is selected for capture.</summary>
    public bool IsCaptureSelected => CaptureCheck.Checked;

    /// <summary>
    /// Initialize as a monitor row from live geometry.
    /// </summary>
    /// <param name="geo">Monitor geometry from the capture backend (null treated as empty).</param>
    public void Initialize(MonitorGeometry geo)
    {
        _geo = geo ?? new MonitorGeometry();
        MonitorIndex = _geo.Index;
        CaptureCheck.Visible = true;
        QualityOverrideCheck.Visible = true;
        QualityBox.Visible = true;
        CaptureCheck.Text = FormatMonitorLabel(_geo);
        MonitorDescriptionLabel.Text = FormatDescription(_geo);
        RebuildScaleTicks();
        UpdateScaleHint();
    }

    /// <summary>
    /// Initialize as full-desktop scale controls (no capture checkbox / quality).
    /// </summary>
    /// <param name="sampleGeo">Reserved for future labels; size always uses the virtual screen.</param>
    public void InitializeDesktop(MonitorGeometry? sampleGeo = null)
    {
        // Because discrete steps use virtual desktop size (full capture bounds), not a single monitor.
        var vs = SystemInformation.VirtualScreen;
        _geo = new MonitorGeometry
        {
            Index = -1,
            Name = "Full desktop",
            Description = "Full desktop",
            LogicalWidth = Math.Max(1, vs.Width),
            LogicalHeight = Math.Max(1, vs.Height),
            PhysicalWidth = Math.Max(1, vs.Width),
            PhysicalHeight = Math.Max(1, vs.Height),
            DetectedScale = 1.0,
            OsScale = 1.0
        };
        // sampleGeo reserved for future labels; size is always virtual screen.
        _ = sampleGeo;

        MonitorIndex = -1;
        CaptureCheck.Visible = false;
        QualityOverrideCheck.Visible = false;
        QualityBox.Visible = false;
        CaptureCheck.Checked = false;
        MonitorDescriptionLabel.Text = "Full desktop output size (when Full desktop capture is enabled)";
        RebuildScaleTicks();
        UpdateScaleHint();
    }

    /// <summary>Apply saved per-monitor settings (or defaults).</summary>
    /// <param name="captureSelected">Whether this monitor is checked for capture.</param>
    /// <param name="ms">Saved overrides, or null for defaults.</param>
    /// <param name="defaultQuality">Global JPEG quality shown when no per-monitor override.</param>
    public void ApplySettings(bool captureSelected, MonitorCaptureSettings? ms, int defaultQuality)
    {
        ms = ms ?? new MonitorCaptureSettings();
        ms.Normalize();

        // Because ApplySettings mutates controls and must not re-enter UpdateScaleHint via events.
        _suppressEvents = true;
        try
        {
            CaptureCheck.Checked = captureSelected;
            SelectScaleNearestPercent(ms.OutputPercent ?? 100.0);

            if (ms.Quality is int q)
            {
                QualityOverrideCheck.Checked = true;
                QualityBox.Enabled = true;
                QualityBox.Value = ClampDecimal(q, QualityBox.Minimum, QualityBox.Maximum);
            }
            else
            {
                QualityOverrideCheck.Checked = false;
                QualityBox.Enabled = false;
                QualityBox.Value = ClampDecimal(defaultQuality, QualityBox.Minimum, QualityBox.Maximum);
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        UpdateScaleHint();
    }

    /// <summary>Apply saved full-desktop settings.</summary>
    /// <param name="ds">Saved desktop scale, or null for 100% default.</param>
    public void ApplyDesktopSettings(DesktopCaptureSettings? ds)
    {
        ds ??= new DesktopCaptureSettings();
        ds.Normalize();

        _suppressEvents = true;
        try
        {
            SelectScaleNearestPercent(ds.OutputPercent ?? 100.0);
        }
        finally
        {
            _suppressEvents = false;
        }

        UpdateScaleHint();
    }

    /// <summary>Read monitor overrides; null when all defaults.</summary>
    /// <returns>
    /// A settings object when quality is overridden or scale is not 100%; otherwise <c>null</c>
    /// so the parent can omit empty entries from the settings map.
    /// </returns>
    public MonitorCaptureSettings? ReadOverrides()
    {
        int? quality = null;
        if (QualityOverrideCheck.Visible && QualityOverrideCheck.Checked)
        {
            quality = (int)QualityBox.Value;
        }

        var selected = GetSelectedScale();
        var outputPct = selected.Percent;

        // Because storing “all defaults” would bloat settings and confuse merge logic.
        if (quality == null && Math.Abs(outputPct - 100.0) < 0.01)
        {
            return null;
        }

        return new MonitorCaptureSettings
        {
            Quality = quality,
            // Because Windows has no X11 scale override; always persist false for cross-platform schema.
            OverrideX11Scale = false,
            OutputPercent = outputPct
        };
    }

    /// <summary>Read desktop scale settings.</summary>
    /// <returns>Desktop capture settings with the selected output percent.</returns>
    public DesktopCaptureSettings ReadDesktopSettings()
    {
        return new DesktopCaptureSettings
        {
            OverrideX11Scale = false,
            OutputPercent = GetSelectedScale().Percent
        };
    }

    /// <summary>
    /// Rebuilds <see cref="_scalesByTick"/> and the trackbar range from current geometry.
    /// </summary>
    /// <remarks>
    /// Scale steps come from <see cref="MonitorScaling.GetMonitorScales"/> (25%-100%).
    /// On failure or empty result, falls back to a single 100% tick.
    /// </remarks>
    private void RebuildScaleTicks()
    {
        // Because RecommendedWidth/Height can be 0 before geometry is ready; clamp for scale math.
        var w = Math.Max(1, _geo.RecommendedWidth);
        var h = Math.Max(1, _geo.RecommendedHeight);
        List<MonitorScale> scales;
        try
        {
            // Because 0.25-1.0 matches Linux and keeps the left end usable (not tiny thumbnails).
            scales = MonitorScaling.GetMonitorScales(w, h, 0.25, 1.0);
        }
        catch
        {
            // Because scale generation can throw on pathological dimensions; keep one safe step.
            scales =
            [
                new MonitorScale(1.0, w, h, "1/1", 100.0, "100%")
            ];
        }

        if (scales.Count == 0)
        {
            scales.Add(new MonitorScale(1.0, w, h, "1/1", 100.0, "100%"));
        }

        // Because TrackBar only has integer Value; map each tick index → discrete MonitorScale.
        _scalesByTick = [];
        for (var i = 0; i < scales.Count; i++)
        {
            _scalesByTick[i] = scales[i];
        }

        _suppressEvents = true;
        try
        {
            OutputSlider.Minimum = 0;
            OutputSlider.Maximum = scales.Count - 1;
            OutputSlider.TickFrequency = 1;
            OutputSlider.SmallChange = 1;
            // Because LargeChange of 4 jumps ~several percent steps without leaping the whole range.
            OutputSlider.LargeChange = 4;
            // Because default preference is full resolution until ApplySettings picks a saved percent.
            OutputSlider.Value = OutputSlider.Maximum;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>
    /// Moves the trackbar to the scale step whose percent is closest to <paramref name="percent"/>.
    /// </summary>
    /// <param name="percent">Desired output percent (e.g. 100, 75, 50).</param>
    private void SelectScaleNearestPercent(double percent)
    {
        if (_scalesByTick.Count == 0)
        {
            return;
        }

        // Because saved percents may not land exactly on a discrete step (legacy or cross-platform).
        var best = 0;
        var bestDist = double.MaxValue;
        foreach (var kv in _scalesByTick)
        {
            var d = Math.Abs(kv.Value.Percent - percent);
            if (d < bestDist)
            {
                bestDist = d;
                best = kv.Key;
            }
        }

        if (best < OutputSlider.Minimum)
        {
            best = OutputSlider.Minimum;
        }

        if (best > OutputSlider.Maximum)
        {
            best = OutputSlider.Maximum;
        }

        OutputSlider.Value = best;
    }

    /// <summary>
    /// Returns the <see cref="MonitorScale"/> for the current trackbar value.
    /// </summary>
    /// <returns>
    /// Mapped scale from <see cref="_scalesByTick"/>, or a synthetic 100% scale if the tick is missing.
    /// </returns>
    private MonitorScale GetSelectedScale()
    {
        if (_scalesByTick.TryGetValue(OutputSlider.Value, out var s))
        {
            return s;
        }

        // Because a missing tick should still produce valid dimensions for labels/save paths.
        var w = Math.Max(1, _geo.RecommendedWidth);
        var h = Math.Max(1, _geo.RecommendedHeight);
        return new MonitorScale(1.0, w, h, "1/1", 100.0, "100%");
    }

    /// <summary>Enables or disables the quality spinner when the override checkbox changes.</summary>
    private void QualityOverrideCheck_CheckedChanged(object sender, EventArgs e)
    {
        QualityBox.Enabled = QualityOverrideCheck.Checked;
    }

    /// <summary>Refreshes the scale label when the user moves the trackbar.</summary>
    private void OutputSlider_ValueChanged(object sender, EventArgs e)
    {
        // Because RebuildScaleTicks / ApplySettings also set Value and must not thrash the label path.
        if (_suppressEvents)
        {
            return;
        }

        UpdateScaleHint();
    }

    /// <summary>
    /// Updates <c>OutputLabel</c> with fraction or percent plus the resulting pixel size.
    /// </summary>
    /// <remarks>
    /// Whole percents and fractions with large denominators (&gt;32) display as percent strings
    /// so the UI stays readable; nice reduced fractions (e.g. 3/4) stay as fractions.
    /// </remarks>
    private void UpdateScaleHint()
    {
        var s = GetSelectedScale();
        // Because whole percents, or fractions with large denominators (>32), are clearer as percent.
        var usePercent = Math.Abs(s.Percent - Math.Round(s.Percent)) < 0.001
                         || FractionDenominator(s.Fraction) > 32;
        var label = usePercent ? s.PercentStr : s.Fraction;
        OutputLabel.Text = $"{label} → {s.Width} x {s.Height}";
    }

    /// <summary>
    /// Parses the denominator of a reduced fraction string like <c>3/4</c>.
    /// </summary>
    /// <param name="fraction">Fraction text (e.g. <c>1/2</c>, <c>3/4</c>), or null/empty.</param>
    /// <returns>
    /// The integer denominator when parsable; otherwise <c>99</c> (treated as “unknown / large”
    /// so <see cref="UpdateScaleHint"/> prefers a percent label).
    /// </returns>
    private static int FractionDenominator(string? fraction)
    {
        // Because missing or non-fraction labels should force percent display (threshold > 32).
        if (string.IsNullOrWhiteSpace(fraction) || !fraction!.Contains("/"))
        {
            return 99; // 99 = “unknown / large denominator”
        }

        // Because only the denominator drives the “is this a nice fraction?” decision.
        return int.TryParse(fraction.Split('/')[1], out var d) ? d : 99; // 99 = “unknown / large denominator”
    }

    /// <summary>Builds the capture-checkbox label: <c>{1-based index}: {name}</c>.</summary>
    /// <param name="geo">Monitor geometry providing index and name.</param>
    /// <returns>User-facing monitor label.</returns>
    private static string FormatMonitorLabel(MonitorGeometry geo)
    {
        var n = string.IsNullOrEmpty(geo.Name) ? ($"Monitor {geo.Index}") : geo.Name;
        return ($"{geo.Index + 1}: {n}");
    }

    /// <summary>Builds the description line under the monitor label.</summary>
    /// <param name="geo">Monitor geometry providing description or logical size.</param>
    /// <returns>Description text, or <c>width x height</c> when description is empty.</returns>
    private static string FormatDescription(MonitorGeometry geo)
    {
        return string.IsNullOrEmpty(geo.Description) ? $"{geo.LogicalWidth} x {geo.LogicalHeight}" : geo.Description;
    }

    /// <summary>Clamps a decimal into <paramref name="min"/>…<paramref name="max"/> inclusive.</summary>
    /// <param name="v">Value to clamp.</param>
    /// <param name="min">Inclusive minimum (e.g. NumericUpDown.Minimum).</param>
    /// <param name="max">Inclusive maximum (e.g. NumericUpDown.Maximum).</param>
    /// <returns>Clamped value safe to assign to a NumericUpDown.</returns>
    private static decimal ClampDecimal(decimal v, decimal min, decimal max)
    {
        // Because NumericUpDown throws if Value is set outside Minimum/Maximum.
        if (v < min)
        {
            return min;
        }

        if (v > max)
        {
            return max;
        }

        return v;
    }
}
