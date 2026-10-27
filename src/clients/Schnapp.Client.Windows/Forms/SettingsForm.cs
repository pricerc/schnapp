namespace Schnapp.Client.Windows.Forms;

using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Schnapp.Client.Core.Config;
using Schnapp.Client.Windows.Capture;
using Schnapp.Client.Windows.Services;

/// <summary>
/// Settings dialog: capture targets/format, per-monitor scale, and paths (Linux SettingsWindow parity).
/// </summary>
/// <remarks>
/// Designer layout in <c>SettingsForm.Designer.cs</c>. Formats: JPEG, PNG, WebP lossy, WebP lossless
/// (WebP via <c>Imazen.WebP</c> NuGet).
/// </remarks>
public partial class SettingsForm : Form
{
    private readonly ClientSettingsStore _store = new();
    private readonly IScreenCapture _capture;
    private readonly List<MonitorSettingsRow> _monitorRows = [];
    private MonitorSettingsRow? _desktopRow;

    /// <summary>
    /// Creates the settings dialog using the live capture backend for monitor enumeration.
    /// </summary>
    public SettingsForm(IScreenCapture capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowPlacement.RestoreOrPlaceNearTray(this, null);
        LoadFromSettings();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        this.DesktopScalePanel.MaximumSize = new System.Drawing.Size(this.Width - 24, this.DesktopScalePanel.MaximumSize.Height);
    }

    private void LoadFromSettings()
    {
        var s = _store.Load();
        s.NormalizeCaptureTargets();

        IntervalBox.Value = Math.Max(IntervalBox.Minimum, Math.Min(IntervalBox.Maximum, s.CaptureIntervalSeconds));
        SkipScreensaverBox.Checked = s.SkipWhenScreensaverActive;
        IdlePauseBox.Value = Math.Max(IdlePauseBox.Minimum, Math.Min(IdlePauseBox.Maximum, s.IdlePauseMinutes));
        FocusDwellBox.Value = Math.Max(FocusDwellBox.Minimum, Math.Min(FocusDwellBox.Maximum, s.WindowFocusDwellSeconds));
        DiscardBlankBox.Checked = s.DiscardBlankDuplicates;
        DiscardIdleBox.Checked = s.DiscardIdleDuplicates;
        IdleDirtyCellsBox.Value = Math.Max(IdleDirtyCellsBox.Minimum, Math.Min(IdleDirtyCellsBox.Maximum, s.IdleMaxDirtyCells));
        CaptureFullBox.Checked = s.CaptureFullDesktop;
        CaptureWindowBox.Checked = s.CaptureActiveWindow;
        CaptureMonitorsBox.Checked = s.CaptureMonitors;

        CaptureFormatBox.Items.Clear();
        CaptureFormatBox.Items.Add("JPEG (default, small, lossy)");
        CaptureFormatBox.Items.Add("PNG (lossless, larger)");
        CaptureFormatBox.Items.Add("WebP (small, lossy)");
        CaptureFormatBox.Items.Add("WebP lossless");
        CaptureFormatBox.SelectedIndex = FormatToIndex(s.CaptureFormat);

        QualityBox.Value = Math.Max(QualityBox.Minimum, Math.Min(QualityBox.Maximum, s.CaptureQuality));
        ApiUrlBox.Text = s.ApiBaseUrl ?? "";
        SpoolDirBox.Text = s.SpoolDirectory ?? "";
        MutationsDirBox.Text = s.MutationsDirectory ?? "";

        FileLogLevelBox.Items.Clear();
        FileLogLevelBox.Items.AddRange(new object[] { "Off", "Error", "Warn", "Info", "Debug" });
        FileLogLevelBox.SelectedIndex = (int)s.FileLogLevel;
        if (FileLogLevelBox.SelectedIndex < 0)
        {
            FileLogLevelBox.SelectedIndex = 3;
        }

        InfoText.Text = "Config: %AppData%\\Schnapp  ·  Data: %LocalAppData%\\Schnapp";

        RebuildMonitorList(s);
        RebuildDesktopRow(s);
        UpdateTargetUi();
        UpdateQualityVisibility();
    }

    /// <summary>Maps stored format to combo index (Linux settings parity).</summary>
    private static int FormatToIndex(CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => 1,
        CaptureImageFormat.Webp => 2,
        CaptureImageFormat.WebpLossless => 3,
        _ => 0,
    };

    /// <summary>Maps combo index to <see cref="CaptureImageFormat"/>.</summary>
    private static CaptureImageFormat IndexToFormat(int index) => index switch
    {
        1 => CaptureImageFormat.Png,
        2 => CaptureImageFormat.Webp,
        3 => CaptureImageFormat.WebpLossless,
        _ => CaptureImageFormat.Jpeg,
    };

    /// <summary>Quality applies to JPEG and lossy WebP only.</summary>
    private void UpdateQualityVisibility()
    {
        var fmt = IndexToFormat(CaptureFormatBox.SelectedIndex);
        var lossy = fmt is CaptureImageFormat.Jpeg or CaptureImageFormat.Webp;
        QualityBox.Enabled = lossy;
        QualityLabel.Enabled = lossy;
    }

    private void CaptureFormatBox_SelectedIndexChanged(object sender, EventArgs e)
        => UpdateQualityVisibility();

    private void RebuildMonitorList(ClientSettings s)
    {
        MonitorListPanel.Controls.Clear();
        _monitorRows.Clear();
        int count;
        try { count = _capture.GetMonitorCount(); }
        catch { count = 0; }

        var selected = new HashSet<int>(s.CaptureMonitorIndexes ?? []);
        var allSelected = selected.Count == 0;

        for (var i = 0; i < count; i++)
        {
            MonitorGeometry geo;
            try { geo = _capture.GetMonitorGeometry(i); }
            catch
            {
                geo = new MonitorGeometry { Index = i, Name = "Monitor " + i, LogicalWidth = 1920, LogicalHeight = 1080, PhysicalWidth = 1920, PhysicalHeight = 1080 };
            }

            var row = new MonitorSettingsRow { Dock = DockStyle.Top };
            row.Initialize(geo);
            s.MonitorSettings.TryGetValue(i.ToString(), out var ms);
            row.ApplySettings(allSelected || selected.Contains(i), ms, s.CaptureQuality);
            _monitorRows.Add(row);
            MonitorListPanel.Controls.Add(row);
            row.BringToFront();
        }
    }

    private void RebuildDesktopRow(ClientSettings s)
    {
        DesktopScalePanel.Controls.Clear();
        _desktopRow = null;

        MonitorGeometry? sample = null;
        try
        {
            if (_capture.GetMonitorCount() > 0)
            {
                sample = _capture.GetMonitorGeometry(0);
            }
        }
        catch { /* ignore */ }

        var row = new MonitorSettingsRow() { Dock = DockStyle.Top };

        row.InitializeDesktop(sample);
        row.ApplyDesktopSettings(s.DesktopSettings);
        _desktopRow = row;
        DesktopScalePanel.Controls.Add(row);
    }

    private void UpdateTargetUi()
    {
        MonitorListPanel.Visible = CaptureMonitorsBox.Checked;
        MonitorPickLabel.Visible = CaptureMonitorsBox.Checked;
        MonitorListPanel.Enabled = CaptureMonitorsBox.Checked;
        MonitorPickLabel.Enabled = CaptureMonitorsBox.Checked;
        DesktopScalePanel.Visible = CaptureFullBox.Checked;
        DesktopScaleLabel.Visible = CaptureFullBox.Checked;
        DesktopScalePanel.Enabled = CaptureFullBox.Checked;
        DesktopScaleLabel.Enabled = CaptureFullBox.Checked;
    }

    private void CaptureMonitorsBox_CheckedChanged(object sender, EventArgs e) => UpdateTargetUi();

    private void CaptureFullBox_CheckedChanged(object sender, EventArgs e) => UpdateTargetUi();

    private void SaveButton_Click(object sender, EventArgs e)
    {
        var s = _store.Load();
        s.CaptureIntervalSeconds = (int)IntervalBox.Value;
        s.SkipWhenScreensaverActive = SkipScreensaverBox.Checked;
        s.IdlePauseMinutes = (int)IdlePauseBox.Value;
        s.WindowFocusDwellSeconds = (int)FocusDwellBox.Value;
        s.DiscardBlankDuplicates = DiscardBlankBox.Checked;
        s.DiscardIdleDuplicates = DiscardIdleBox.Checked;
        s.IdleMaxDirtyCells = (int)IdleDirtyCellsBox.Value;
        s.CaptureFullDesktop = CaptureFullBox.Checked;
        s.CaptureActiveWindow = CaptureWindowBox.Checked;
        s.CaptureMonitors = CaptureMonitorsBox.Checked;
        s.CaptureFormat = IndexToFormat(CaptureFormatBox.SelectedIndex);
        s.CaptureQuality = (int)QualityBox.Value;
        s.ApiBaseUrl = (ApiUrlBox.Text ?? "").Trim();
        s.SpoolDirectory = string.IsNullOrWhiteSpace(SpoolDirBox.Text) ? null : SpoolDirBox.Text.Trim();
        s.MutationsDirectory = string.IsNullOrWhiteSpace(MutationsDirBox.Text) ? null : MutationsDirBox.Text.Trim();
        if (FileLogLevelBox.SelectedIndex >= 0)
        {
            s.FileLogLevel = (ClientSettings.ClientLogLevel)FileLogLevelBox.SelectedIndex;
        }

        var indexes = new List<int>();
        var allChecked = true;
        var monitorSettings = new Dictionary<string, MonitorCaptureSettings>();
        foreach (var row in _monitorRows)
        {
            if (row.IsCaptureSelected)
            {
                indexes.Add(row.MonitorIndex);
            }
            else
            {
                allChecked = false;
            }

            var ovr = row.ReadOverrides();
            if (ovr != null)
            {
                monitorSettings[row.MonitorIndex.ToString()] = ovr;
            }
        }
        // Empty = all monitors (same semantics as Linux)
        s.CaptureMonitorIndexes = allChecked || indexes.Count == 0 ? [] : indexes;
        s.MonitorSettings = monitorSettings;

        if (_desktopRow != null)
        {
            s.DesktopSettings = _desktopRow.ReadDesktopSettings();
        }

        s.NormalizeCaptureTargets();
        _store.Save(s);
        AppLog.InitFileFromSettings(s);
        AppLog.Info("Settings saved");
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CloseButton_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
