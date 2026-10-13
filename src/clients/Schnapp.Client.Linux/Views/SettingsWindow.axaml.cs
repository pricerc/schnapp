namespace Schnapp.Client.Linux.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Schnapp.Client.Core.Config;
using Schnapp.Client.Linux.Capture;
using Schnapp.Client.Linux.Services;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Client settings: API URL, combinable capture targets, per-monitor scale/quality, offline paths.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ClientSettingsStore _store = new();
    private readonly List<MonitorSettingsRow> _monitorRows = [];
    private MonitorSettingsRow? _desktopRow;
    private readonly X11ScreenInfoProvider _screenInfo = new(new AvaloniaScreenInfoProvider());

    public SettingsWindow()
    {
        InitializeComponent();
        BuildMonitorList();
        Reload();
        Opened += PositionNearTray;
    }

    private void PositionNearTray(object? sender, EventArgs e)
    {
        Opened -= PositionNearTray;
        var screens = Screens.All;
        if (screens.Count == 0)
        {
            return;
        }


        var screen = screens
            .OrderByDescending(s => s.Bounds.Right)
            .ThenByDescending(s => s.Bounds.Bottom)
            .First();

        var wa = screen.WorkingArea;
        double scale = screen.Scaling;
        double w = Width > 0 ? Width : 540;
        double h = Height > 0 ? Height : 520;
        int x = wa.Right - (int)(w * scale) - (int)(16 * scale);
        int y = wa.Bottom - (int)(h * scale) - (int)(48 * scale);
        x = Math.Max(wa.X, x);
        y = Math.Max(wa.Y, y);
        Position = new PixelPoint(x, y);
    }

    private void BuildMonitorList()
    {
        MonitorListPanel.Children.Clear();
        _monitorRows.Clear();
        _desktopRow = null;

        var count = Math.Max(1, _screenInfo.GetMonitorCount());
        for (var i = 0; i < count; i++)
        {
            var geo = _screenInfo.GetMonitorGeometry(i);
            var row = new MonitorSettingsRow();
            row.Initialize(geo);
            _monitorRows.Add(row);
            MonitorListPanel.Children.Add(row);
        }

        // Full-desktop scale controls (always built; visibility tied to full-desktop checkbox)
        var deskAuto = _screenInfo.GetDesktopAutoScale();
        int deskW = 0, deskH = 0;
        for (var i = 0; i < count; i++)
        {
            var b = _screenInfo.GetMonitorBounds(i);
            deskW = Math.Max(deskW, b.X + b.Width);
            deskH = Math.Max(deskH, b.Y + b.Height);
        }
        var deskGeo = new MonitorGeometry
        {
            Index = -1,
            Name = "Full desktop",
            LogicalWidth = Math.Max(1, deskW),
            LogicalHeight = Math.Max(1, deskH),
            PhysicalWidth = Math.Max(1, (int)Math.Round(deskW * deskAuto)),
            PhysicalHeight = Math.Max(1, (int)Math.Round(deskH * deskAuto)),
            X11Scale = deskAuto
        };
        _desktopRow = new MonitorSettingsRow();
        _desktopRow.Initialize(deskGeo);
        _desktopRow.ShowCaptureCheck = false;
        DesktopScalePanel.Children.Clear();
        DesktopScalePanel.Children.Add(_desktopRow);
    }

    private void Reload()
    {
        var s = _store.Load();
        s.NormalizeCaptureTargets();
        ApiUrlBox.Text = s.ApiBaseUrl;
        IntervalBox.Value = Math.Clamp(s.CaptureIntervalSeconds, 15, 3600);
        CaptureFullBox.IsChecked = s.CaptureFullDesktop;
        CaptureMonitorsBox.IsChecked = s.CaptureMonitors;
        CaptureWindowBox.IsChecked = s.CaptureActiveWindow;
        ApplyMonitorSelection(s);
        _desktopRow?.ApplyDesktopSettings(s.DesktopSettings);
        UpdateMonitorPickVisibility();
        SkipScreensaverBox.IsChecked = s.SkipWhenScreensaverActive;
        IdlePauseBox.Value = Math.Clamp(s.IdlePauseMinutes, 0, 1440);
        DiscardBlankBox.IsChecked = s.DiscardBlankDuplicates;
        DiscardIdleBox.IsChecked = s.DiscardIdleDuplicates;
        IdleDirtyCellsBox.Value = Math.Clamp(s.IdleMaxDirtyCells, 1, 20);
        CaptureFormatBox.SelectedIndex = FormatToIndex(s.CaptureFormat);
        QualityBox.Value = Math.Clamp(s.CaptureQuality, 1, 100);
        UpdateQualityVisibility();
        SpoolDirBox.Text = s.SpoolDirectory ?? "";
        MutationsDirBox.Text = s.MutationsDirectory ?? "";
        FileLogLevelBox.SelectedIndex = Math.Clamp((int)s.FileLogLevel, 0, 4);
        var token = _store.LoadToken() != null ? "set" : "missing";
        InfoText.Text =
            $"Login: {s.LoginIdentity ?? "(none)"}  token={token}\n" +
            $"Machine: {s.MachineName ?? "(none)"}  {s.MachineId}\n" +
            $"Spool: {ClientPaths.GetSpoolDirectory(s.SpoolDirectory)}\n" +
            $"Mutations: {ClientPaths.GetMutationsDirectory(s.MutationsDirectory)}\n" +
            "Path changes apply after restart. Env: SCHNAPP_SPOOL_DIR, SCHNAPP_MUTATIONS_DIR.";
    }

    private void OnCaptureTargetsChanged(object? sender, RoutedEventArgs e)
        => UpdateMonitorPickVisibility();

    private void OnShowMonitorSettingsChanged(object? sender, RoutedEventArgs e)
        => UpdateMonitorPickVisibility();

    private void OnCaptureFormatChanged(object? sender, SelectionChangedEventArgs e)
        => UpdateQualityVisibility();

    private void UpdateMonitorPickVisibility()
    {
        var mon = CaptureMonitorsBox.IsChecked == true;
        ShowMonitorSettingsBox.IsVisible = mon;
        var showDetails = mon && ShowMonitorSettingsBox.IsChecked == true;
        MonitorPickLabel.IsVisible = showDetails;
        MonitorListPanel.IsVisible = showDetails;

        var full = CaptureFullBox.IsChecked == true;
        DesktopScaleLabel.IsVisible = full;
        DesktopScalePanel.IsVisible = full;
    }

    private void UpdateQualityVisibility()
    {
        var fmt = IndexToFormat(CaptureFormatBox.SelectedIndex);
        var lossy = fmt is CaptureImageFormat.Jpeg or CaptureImageFormat.Webp;
        QualityLabel.IsVisible = lossy;
        QualityBox.IsVisible = lossy;
        foreach (var row in _monitorRows)
        {
            row.SetQualityVisible(lossy);
        }

    }

    private void ApplyMonitorSelection(ClientSettings s)
    {
        var set = new HashSet<int>(s.CaptureMonitorIndexes ?? []);
        var mon = s.MonitorSettings ?? [];

        foreach (var row in _monitorRows)
        {
            mon.TryGetValue(row.MonitorIndex.ToString(), out var ms);
            var selected = set.Count > 0 && set.Contains(row.MonitorIndex);
            row.ApplySettings(selected, ms, s.CaptureQuality);
        }
    }

    private List<int> ReadSelectedMonitors()
    {
        return [.. _monitorRows
            .Where(r => r.IsCaptureSelected)
            .Select(r => r.MonitorIndex)
            .OrderBy(i => i)];
    }

    private Dictionary<string, MonitorCaptureSettings> ReadMonitorSettings()
    {
        var dict = new Dictionary<string, MonitorCaptureSettings>();
        foreach (var row in _monitorRows)
        {
            var ms = row.ReadOverrides();
            if (ms != null)
            {
                dict[row.MonitorIndex.ToString()] = ms;
            }

        }
        return dict;
    }

    private static int FormatToIndex(CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Png => 1,
        CaptureImageFormat.Webp => 2,
        CaptureImageFormat.WebpLossless => 3,
        _ => 0
    };

    private static CaptureImageFormat IndexToFormat(int index) => index switch
    {
        1 => CaptureImageFormat.Png,
        2 => CaptureImageFormat.Webp,
        3 => CaptureImageFormat.WebpLossless,
        _ => CaptureImageFormat.Jpeg
    };

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var s = _store.Load();
        s.ApiBaseUrl = ApiUrlBox.Text?.Trim() ?? s.ApiBaseUrl;
        s.CaptureIntervalSeconds = (int)Math.Clamp(IntervalBox.Value ?? 300, 15, 3600);
        s.CaptureFullDesktop = CaptureFullBox.IsChecked == true;
        s.CaptureMonitors = CaptureMonitorsBox.IsChecked == true;
        s.CaptureActiveWindow = CaptureWindowBox.IsChecked == true;
        s.CaptureMonitorIndexes = ReadSelectedMonitors();
        s.MonitorSettings = ReadMonitorSettings();
        s.DesktopSettings = _desktopRow?.ReadDesktopSettings() ?? new DesktopCaptureSettings();
        s.CaptureMode = null;

        if (!s.CaptureFullDesktop && !s.CaptureMonitors && !s.CaptureActiveWindow)
        {
            InfoText.Text = "Enable at least one of: full desktop, monitors, or active window.";
            return;
        }

        s.SkipWhenScreensaverActive = SkipScreensaverBox.IsChecked == true;
        s.IdlePauseMinutes = (int)Math.Clamp(IdlePauseBox.Value ?? 0, 0, 1440);
        s.DiscardBlankDuplicates = DiscardBlankBox.IsChecked == true;
        s.DiscardIdleDuplicates = DiscardIdleBox.IsChecked == true;
        s.IdleMaxDirtyCells = (int)Math.Clamp(IdleDirtyCellsBox.Value ?? 2, 1, 20);
        s.CaptureFormat = IndexToFormat(CaptureFormatBox.SelectedIndex);
        s.CaptureQuality = (int)Math.Clamp(QualityBox.Value ?? 80, 1, 100);
        s.SpoolDirectory = NullIfEmpty(SpoolDirBox.Text);
        s.MutationsDirectory = NullIfEmpty(MutationsDirBox.Text);
        s.FileLogLevel = (ClientSettings.ClientLogLevel)Math.Clamp(FileLogLevelBox.SelectedIndex, 0, 4);
        _store.Save(s);
        AppLog.InitFileFromSettings(s);
        AppLog.Info(
            $"Settings updated: full={s.CaptureFullDesktop} monitors={s.CaptureMonitors} window={s.CaptureActiveWindow}");
        InfoText.Text = "Saved. Restart the tray app if you changed spool/mutation paths.";
        Reload();
    }

    private static string? NullIfEmpty(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
