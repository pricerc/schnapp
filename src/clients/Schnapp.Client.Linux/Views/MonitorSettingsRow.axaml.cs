namespace Schnapp.Client.Linux.Views;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Schnapp.Client.Core.Config;
using Schnapp.Client.Linux.Capture;

using System;
using System.Text;

/// <summary>
/// Short per-monitor row: capture toggle, effective size, quality, scale output, CRTC override.
/// </summary>
/// <remarks>
/// Scale steps come from <see cref="MonitorScaling.GetMonitorScales"/> (ascending 25%→100%).
/// The Avalonia slider uses percent values with Minimum=25 and Maximum=100 so left is small
/// and right is full resolution.
/// </remarks>
public partial class MonitorSettingsRow : UserControl
{
    /// <summary>Recommended monitor scales keyed by output width in pixels.</summary>
    /// <remarks>
    /// Recommended = physical when fat FB, logical when skinny.
    /// Because GetMonitorScales is ascending, insertion order into the dictionary preserves
    /// smallest→largest for tick lists.
    /// </remarks>
    private Dictionary<int, MonitorScale> RecommendedScales =>
        _geo.RecommendedWidth > 0 && _geo.RecommendedHeight > 0
        ? MonitorScaling.GetMonitorScales(_geo.RecommendedWidth, _geo.RecommendedHeight, 0.25, 1.0)
            .ToDictionary(s => s.Width, s => s)
        : [];

    /// <summary>
    /// Slider tick positions as percent values (25-100), matching <see cref="RecommendedScales"/>.
    /// </summary>
    private AvaloniaList<double> RecommendedScaleTicks => [.. RecommendedScales.Values.Select(s => s.Ratio * 100).ToList()];

    /// <summary>Geometry for this row (monitor or desktop sample).</summary>
    private MonitorGeometry _geo = new();

    /// <summary>When true, ignore slider/toggle handlers during programmatic apply.</summary>
    private bool _suppressEvents;

    /// <summary>Whether the capture checkbox is shown (false for full-desktop row).</summary>
    private bool _showCaptureCheck = true;

    /// <summary>
    /// True when CRTC is larger than physical (fat FB / XLibre content-down style).
    /// </summary>
    /// <remarks>
    /// Because a fat framebuffer means X11 scale &lt; 1 and logical size exceeds physical;
    /// the override toggle then chooses CRTC vs panel pixels.
    /// </remarks>
    private bool IsFatFramebuffer =>
        _geo.LogicalWidth > _geo.PhysicalWidth + 1
        || _geo.LogicalHeight > _geo.PhysicalHeight + 1
        || (_geo.X11Scale > 0 && _geo.X11Scale < 1.0 - 0.001);

    /// <summary>Designer / XAML constructor.</summary>
    public MonitorSettingsRow()
    {
        InitializeComponent();
    }

    /// <summary>Zero-based monitor index; negative for non-monitor rows when used that way.</summary>
    public int MonitorIndex { get; private set; }

    /// <summary>Whether capture is enabled for this monitor.</summary>
    public bool IsCaptureSelected => CaptureCheck.IsChecked == true;

    /// <summary>When false, hide capture checkbox (e.g. desktop-only row).</summary>
    public bool ShowCaptureCheck
    {
        get => _showCaptureCheck;
        set
        {
            _showCaptureCheck = value;
            CaptureCheck.IsVisible = value;
        }
    }

    public void Initialize(MonitorGeometry geo)
    {
        _geo = geo;
        MonitorIndex = geo.Index;

        OutputSlider.Ticks = [.. RecommendedScaleTicks];

        CaptureCheck.Content = GetMonitorLabel(geo);
        MonitorDescriptionLabel.Text = GetMonitorDescription(geo);

        _suppressEvents = true;
        try
        {
            OverrideX11Check.IsChecked = false;
            OutputSlider.Value = 100;
            ApplyOverrideEnabledState();
        }
        finally
        {
            _suppressEvents = false;
        }
        UpdateGeometryLabel();
        UpdateScaleLabel();
    }

    /// <summary>
    /// Get a short label for the monitor row, e.g. "1: DisplayPort-1 - AOC 31" (25601440)".
    /// </summary>
    /// <param name="geo"></param>
    /// <returns></returns>
    private static string GetMonitorLabel(MonitorGeometry geo)
    {
        if (geo.Index < 0)
        {
            return geo.Name;
        }

        StringBuilder resultBuilder = new(100);
        resultBuilder.Append($"{geo.Index + 1}");
        if (!string.IsNullOrEmpty(geo.Name))
        {
            resultBuilder.Append($": {geo.Name}");
        }

        return resultBuilder.ToString();
    }

    /// <summary>
    /// Get a short label for the monitor row, e.g. "1: DisplayPort-1 - AOC 31" (2560×1440)".
    /// </summary>
    /// <param name="geo"></param>
    /// <returns></returns>
    private static string GetMonitorDescription(MonitorGeometry geo)
    {
        if (geo.Index < 0)
        {
            return geo.Name;
        }

        StringBuilder resultBuilder = new(100);
        if (!string.IsNullOrEmpty(geo.Description))
        {
            resultBuilder.Append($"{geo.Description}");
        }

        resultBuilder.Append($" - {geo.PhysicalWidth} x {geo.PhysicalHeight}");

        return resultBuilder.ToString();
    }

    /// <summary>
    /// Apply saved monitor capture settings to the row controls, or defaults if null.
    /// </summary>
    /// <param name="captureSelected"></param>
    /// <param name="ms"></param>
    /// <param name="defaultQuality"></param>
    public void ApplySettings(bool captureSelected, MonitorCaptureSettings? ms, int defaultQuality)
    {
        CaptureCheck.IsChecked = captureSelected;
        ms ??= new MonitorCaptureSettings();
        ms.Normalize();

        _suppressEvents = true;
        try
        {
            ApplyOverrideEnabledState();
            // Override only meaningful on fat FB; ignore saved override when skinny.
            OverrideX11Check.IsChecked = IsFatFramebuffer && ms.OverrideX11Scale;

            var selectedScale = RecommendedScales.Values.MinBy(s => Math.Abs(s.Percent - ms.OutputPercent ?? 100.0));

            OutputSlider.Value = selectedScale.Percent;

            if (ms.Quality is int q)
            {
                QualityOverrideCheck.IsChecked = true;
                QualityBox.IsEnabled = true;
                QualityBox.Value = Math.Clamp(q, 1, 100);
            }
            else
            {
                QualityOverrideCheck.IsChecked = false;
                QualityBox.IsEnabled = false;
                QualityBox.Value = Math.Clamp(defaultQuality, 1, 100);
            }
        }
        finally
        {
            _suppressEvents = false;
        }
        UpdateGeometryLabel();
        UpdateScaleLabel();
    }

    /// <summary>
    /// Desktop row uses DesktopCaptureSettings (no quality/capture).
    /// </summary>
    /// <param name="ds">The desktop capture settings to apply, or null for defaults.</param>
    public void ApplyDesktopSettings(DesktopCaptureSettings? ds)
    {
        ShowCaptureCheck = false;
        QualityOverrideCheck.IsVisible = false;
        QualityBox.IsVisible = false;
        ds ??= new DesktopCaptureSettings();
        ds.Normalize();

        _suppressEvents = true;
        try
        {
            ApplyOverrideEnabledState();
            OverrideX11Check.IsChecked = IsFatFramebuffer && ds.OverrideX11Scale;
            OutputSlider.Value = ds.OutputPercent is double op
                ? Math.Clamp(op, 25, 100)
                : 100;
        }
        finally
        {
            _suppressEvents = false;
        }
        UpdateGeometryLabel();        
        UpdateScaleLabel();
    }

    public void SetQualityVisible(bool visible)
    {
        if (!ShowCaptureCheck)
        {
            return;
        }

        QualityOverrideCheck.IsVisible = visible;
        QualityBox.IsVisible = visible;
    }

    /// <summary>
    /// Read the current row settings into a MonitorCaptureSettings object, or null if no overrides.
    /// </summary>
    /// <returns>The current monitor capture settings, or null if no overrides are set.</returns>
    public MonitorCaptureSettings? ReadOverrides()
    {
        int? quality = null;
        if (QualityOverrideCheck.IsVisible && QualityOverrideCheck.IsChecked == true)
        {
            quality = (int) Math.Clamp(QualityBox.Value ?? 80, 1, 100);
        }

        // Only persist override when fat FB and checked.
        bool ovr = IsFatFramebuffer && OverrideX11Check.IsChecked == true;

        double? outputPct = Math.Clamp(OutputSlider.Value, 25, 100);

        if (quality == null && !ovr && outputPct == null)
        {
            return null;
        }

        return new MonitorCaptureSettings
        {
            Quality = quality,
            OverrideX11Scale = ovr,
            OutputPercent = outputPct
        };
    }

    /// <summary>
    /// Read the current row settings into a DesktopCaptureSettings object.
    /// </summary>
    /// <returns>The current desktop capture settings.</returns>
    public DesktopCaptureSettings ReadDesktopSettings()
    {
        return new DesktopCaptureSettings
        {
            OverrideX11Scale = IsFatFramebuffer && OverrideX11Check.IsChecked == true,
            OutputPercent = Math.Clamp(OutputSlider.Value, 25, 100)
        };
    }

    private void ApplyOverrideEnabledState()
    {
        // Skinny FB / content-up: CRTC ≤ physical, auto scale 1 — override disabled.
        OverrideX11Check.IsEnabled = IsFatFramebuffer;
        if (!IsFatFramebuffer)
        {
            OverrideX11Check.IsChecked = false;
        }
    }

    private void OnQualityOverrideChanged(object? sender, RoutedEventArgs e)
    {
        QualityBox.IsEnabled = QualityOverrideCheck.IsChecked == true;
    }

    private void OnOverrideX11Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        UpdateGeometryLabel();
        UpdateScaleLabel();
    }

    private void OnOutputSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (e.Property == RangeBase.ValueProperty)
        {
            UpdateScaleLabel();
        }
    }

    private void UpdateGeometryLabel()
    {
        var useCrtc = IsFatFramebuffer && OverrideX11Check.IsChecked == true;

        // Base for 100% output:
        // override (fat FB): CRTC/logical
        // auto: recommended (physical when fat, logical when skinny)
        int baseW, baseH;
        if (useCrtc)
        {
            baseW = _geo.LogicalWidth;
            baseH = _geo.LogicalHeight;
        }
        else
        {
            baseW = _geo.RecommendedWidth;
            baseH = _geo.RecommendedHeight;
        }

        // GeometryLabel: show the selected base resolution only (short).
        GeometryLabel.Text = $" ({baseW} x {baseH})";
    }

    private void UpdateScaleLabel()
    {
        var selectedScale = RecommendedScales.Values.MinBy(s => Math.Abs(s.Ratio * 100 - (double) OutputSlider.Value));

        if (Math.Abs(selectedScale.Percent - Math.Round(selectedScale.Percent)) < double.Epsilon)
        {
            OutputLabel.Text = $"{selectedScale.PercentStr} - {selectedScale.Width} x {selectedScale.Height}";
        }
        else
        {
            OutputLabel.Text = $"{selectedScale.Fraction} - {selectedScale.Width} x {selectedScale.Height}";
        }
    }
}
