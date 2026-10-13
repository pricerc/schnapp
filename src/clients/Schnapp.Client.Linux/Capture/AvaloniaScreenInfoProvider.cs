namespace Schnapp.Client.Linux.Capture;

using System;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

using Schnapp.Client.Linux.Services;

/// <summary>
/// Monitor count/bounds via Avalonia <see cref="Avalonia.Controls.Screens"/>.
/// Scale detection is not available here (always 1.0); prefer X11 for RandR scale.
/// </summary>
public sealed class AvaloniaScreenInfoProvider : IScreenInfoProvider
{
    /// <inheritdoc />
    public int GetMonitorCount()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Screens != null)
            {
                var n = Math.Max(1, desktop.MainWindow.Screens.All.Count);
                AppLog.Debug($"ScreenInfo: GetMonitorCount -> {n}");
                return n;
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("ScreenInfo: GetMonitorCount threw: " + ex.Message);
        }

        AppLog.Debug("ScreenInfo: GetMonitorCount fallback=1");
        return 1;
    }

    /// <inheritdoc />
    public PixelRect GetMonitorBounds(int monitorIndex)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Screens != null)
            {
                var screens = desktop.MainWindow.Screens.All;
                if (monitorIndex >= 0 && monitorIndex < screens.Count)
                {
                    var b = screens[monitorIndex].Bounds;
                    AppLog.Debug($"ScreenInfo: bounds[{monitorIndex}]={b.X},{b.Y} {b.Width}x{b.Height}");
                    return b;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("ScreenInfo: GetMonitorBounds threw: " + ex.Message);
        }
        AppLog.Debug("ScreenInfo: GetMonitorBounds fallback 0,0 1920x1080");
        return new PixelRect(0, 0, 1920, 1080);
    }

    /// <inheritdoc />
    public string GetMonitorName(int monitorIndex)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Screens != null)
            {
                var screens = desktop.MainWindow.Screens.All;
                if (monitorIndex >= 0 && monitorIndex < screens.Count)
                {
                    var s = screens[monitorIndex];
                    if (!string.IsNullOrWhiteSpace(s.DisplayName))
                    {
                        return s.DisplayName!;
                    }

                    if (s.IsPrimary)
                    {
                        return "primary";
                    }

                    AppLog.Debug($"ScreenInfo: monitor[{monitorIndex}] name=Monitor {monitorIndex + 1} (no display name)");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("ScreenInfo: GetMonitorName threw: " + ex.Message);
        }

        return $"Monitor {monitorIndex + 1}";
    }

    /// <summary>Avalonia is not a scale source; always 1.0 (RandR via X11 provider).</summary>
    public double GetMonitorDetectedScale(int monitorIndex) => 1.0;

    /// <inheritdoc />
    public MonitorGeometry GetMonitorGeometry(int monitorIndex)
    {
        var b = GetMonitorBounds(monitorIndex);
        return new MonitorGeometry
        {
            Index = monitorIndex,
            Name = GetMonitorName(monitorIndex),
            LogicalX = b.X,
            LogicalY = b.Y,
            LogicalWidth = b.Width,
            LogicalHeight = b.Height,
            PhysicalWidth = b.Width,
            PhysicalHeight = b.Height
        };
    }

    /// <inheritdoc />
    public double GetDesktopAutoScale() => 1.0;

    /// <inheritdoc />
    public int? FindMonitorAt(int rootX, int rootY)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Screens != null)
            {
                var screens = desktop.MainWindow.Screens.All;
                int? best = null;
                var bestArea = -1L;
                for (var i = 0; i < screens.Count; i++)
                {
                    var b = screens[i].Bounds;
                    if (rootX < b.X || rootY < b.Y || rootX >= b.X + b.Width || rootY >= b.Y + b.Height)
                    {
                        continue;
                    }

                    long area = (long)b.Width * b.Height;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = i;
                    }
                }
                return best;
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("ScreenInfo: FindMonitorAt threw: " + ex.Message);
        }

        return null;
    }
}
