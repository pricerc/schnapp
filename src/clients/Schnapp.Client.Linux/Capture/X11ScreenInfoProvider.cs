namespace Schnapp.Client.Linux.Capture;

using System;

using Avalonia;

using Schnapp.Client.Linux.Services;
using System.Linq;

/// <summary>
/// Monitor geometry via Xinerama + libXrandr (scale). Avalonia only for optional names.
/// </summary>
public sealed class X11ScreenInfoProvider : IScreenInfoProvider
{
    private readonly IScreenInfoProvider? _fallback;
    private X11Native.XineramaScreenInfo[]? _xinerama;
    private X11Native.XrandrOutputInfo[]? _randr;
    private MonitorGeometry[]? _geometry;
    private DateTime _cacheUtc = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);
    private bool _loggedOnce;

    /// <summary>
    /// Creates a provider that prefers Xinerama/RandR, falling back to <paramref name="fallback"/> when X11 data is missing.
    /// </summary>
    /// <param name="fallback">Optional secondary source (e.g. Avalonia) for count/bounds/names.</param>
    public X11ScreenInfoProvider(IScreenInfoProvider? fallback = null)
    {
        _fallback = fallback;
    }

    /// <inheritdoc />
    public int GetMonitorCount()
    {
        EnsureCache();
        if (_xinerama is { Length: > 0 })
        {

            return _xinerama.Length;
        }


        return _fallback?.GetMonitorCount() ?? 1;
    }

    /// <inheritdoc />
    public PixelRect GetMonitorBounds(int monitorIndex)
    {
        EnsureCache();
        if (_xinerama != null && monitorIndex >= 0 && monitorIndex < _xinerama.Length)
        {
            var s = _xinerama[monitorIndex];
            return new PixelRect(s.x_org, s.y_org, s.width, s.height);
        }
        return _fallback?.GetMonitorBounds(monitorIndex) ?? new PixelRect(0, 0, 1920, 1080);
    }

    /// <inheritdoc />
    public string GetMonitorName(int monitorIndex)
    {
        var g = GetMonitorGeometry(monitorIndex);
        if (!string.IsNullOrWhiteSpace(g.Name) && !g.Name.StartsWith("Monitor ", StringComparison.Ordinal))
        {

            return g.Name;
        }


        return _fallback?.GetMonitorName(monitorIndex) ?? $"Monitor {monitorIndex + 1}";
    }

    /// <inheritdoc />
    public double GetMonitorDetectedScale(int monitorIndex) =>
        GetMonitorGeometry(monitorIndex).X11Scale;

    /// <inheritdoc />
    public MonitorGeometry GetMonitorGeometry(int monitorIndex)
    {
        EnsureCache();
        if (_geometry != null && monitorIndex >= 0 && monitorIndex < _geometry.Length)
        {

            return _geometry[monitorIndex];
        }


        var b = GetMonitorBounds(monitorIndex);
        return new MonitorGeometry
        {
            Index = monitorIndex,
            Name = $"Monitor {monitorIndex + 1}",
            LogicalX = b.X,
            LogicalY = b.Y,
            LogicalWidth = b.Width,
            LogicalHeight = b.Height,
            PhysicalWidth = b.Width,
            PhysicalHeight = b.Height,
            X11Scale = 1.0
        };
    }

    /// <inheritdoc />
    public double GetDesktopAutoScale()
    {
        EnsureCache();
        if (_geometry == null || _geometry.Length == 0)
        {
            return 1.0;
        }
        var min = 1.0;
        foreach (var g in _geometry)
        {
            if (g.X11Scale > 0 && g.X11Scale < min)
            {
                min = g.X11Scale;
            }

        }
        return min;
    }

    /// <inheritdoc />
    public int? FindMonitorAt(int rootX, int rootY)
    {
        EnsureCache();
        if (_xinerama is { Length: > 0 })
        {
            int? best = null;
            var bestArea = -1L;
            for (var i = 0; i < _xinerama.Length; i++)
            {
                var s = _xinerama[i];
                int x = s.x_org, y = s.y_org, w = s.width, h = s.height;
                if (rootX < x || rootY < y || rootX >= x + w || rootY >= y + h)
                {
                    continue;
                }


                long area = (long) w * h;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = i;
                }
            }
            return best;
        }
        return _fallback?.FindMonitorAt(rootX, rootY);
    }

    private void EnsureCache()
    {
        var now = DateTime.UtcNow;
        if (_geometry != null && now - _cacheUtc < CacheTtl)
        {
            return;
        }


        if (!X11Native.IsAvailable)
        {
            _xinerama = Array.Empty<X11Native.XineramaScreenInfo>();
            _randr = Array.Empty<X11Native.XrandrOutputInfo>();
            _geometry = Array.Empty<MonitorGeometry>();
            _cacheUtc = now;
            return;
        }

        lock (X11Native.Sync)
        {
            _xinerama = X11Native.QueryXineramaScreensUnlocked(X11Native.Display);
            _randr = X11Native.QueryXrandrOutputsUnlocked(X11Native.Display);
            _geometry = BuildGeometry(_xinerama, _randr);
            _cacheUtc = now;
            LogOnce(_xinerama, _randr, _geometry);
        }
    }

    private static MonitorGeometry[] BuildGeometry(
        X11Native.XineramaScreenInfo[] xinerama,
        X11Native.XrandrOutputInfo[] randr)
    {
        if (xinerama.Length == 0)
        {
            // Fall back to RandR outputs as monitors
            if (randr.Length == 0)
            {

                return Array.Empty<MonitorGeometry>();
            }


            var list = new MonitorGeometry[randr.Length];
            for (var i = 0; i < randr.Length; i++)
            {
                var o = randr[i];
                list[i] = FromRandr(i, o);
            }
            return list;
        }

        var result = new MonitorGeometry[xinerama.Length];
        for (var i = 0; i < xinerama.Length; i++)
        {
            var s = xinerama[i];
            var match = FindBestRandr(randr, s.x_org, s.y_org, s.width, s.height);
            if (match != null)
            {
                result[i] = new MonitorGeometry
                {
                    Index = i,
                    Name = match.Name,
                    Description = match.Description ?? "",
                    LogicalX = s.x_org,
                    LogicalY = s.y_org,
                    LogicalWidth = s.width,
                    LogicalHeight = s.height,
                    PhysicalWidth = match.PhysicalWidth > 0 ? match.PhysicalWidth : s.width,
                    PhysicalHeight = match.PhysicalHeight > 0 ? match.PhysicalHeight : s.height,
                    Rotation = match.Rotation,
                    X11Scale = match.X11Scale
                };
            }
            else
            {
                result[i] = new MonitorGeometry
                {
                    Index = i,
                    Name = i == 0 ? "primary" : $"Monitor {i + 1}",
                    LogicalX = s.x_org,
                    LogicalY = s.y_org,
                    LogicalWidth = s.width,
                    LogicalHeight = s.height,
                    PhysicalWidth = s.width,
                    PhysicalHeight = s.height,
                    X11Scale = 1.0
                };
            }
        }
        return result;
    }

    private static MonitorGeometry FromRandr(int index, X11Native.XrandrOutputInfo o) => new()
    {
        Index = index,
        Name = o.Name,
        Description = o.Description ?? "",
        LogicalX = o.X,
        LogicalY = o.Y,
        LogicalWidth = o.CurrentWidth,
        LogicalHeight = o.CurrentHeight,
        PhysicalWidth = o.PhysicalWidth > 0 ? o.PhysicalWidth : o.CurrentWidth,
        PhysicalHeight = o.PhysicalHeight > 0 ? o.PhysicalHeight : o.CurrentHeight,
        Rotation = o.Rotation,
        X11Scale = o.X11Scale
    };


    /// <summary>
    /// Finds the RandR output with the largest area overlap for a Xinerama screen rectangle.
    /// </summary>
    private static X11Native.XrandrOutputInfo? FindBestRandr(
        X11Native.XrandrOutputInfo[] randr,
        int x, int y, int w, int h)
    {
        X11Native.XrandrOutputInfo? best = null;
        var bestOverlap = 0L;
        foreach (var o in randr)
        {
            var ox = Math.Max(0, Math.Min(x + w, o.X + o.CurrentWidth) - Math.Max(x, o.X));
            var oy = Math.Max(0, Math.Min(y + h, o.Y + o.CurrentHeight) - Math.Max(y, o.Y));
            long area = (long) ox * oy;
            if (area > bestOverlap)
            {
                bestOverlap = area;
                best = o;
            }
        }
        return bestOverlap > 0 ? best : null;
    }

    private void LogOnce(
        X11Native.XineramaScreenInfo[] xinerama,
        X11Native.XrandrOutputInfo[] randr,
        MonitorGeometry[] geometry)
    {
        if (_loggedOnce)
        {
            return;
        }


        _loggedOnce = true;

        if (randr.Length == 0)
        {
            AppLog.Info("XRandR scale detect: no connected outputs");
            return;
        }

        AppLog.Info($"XRandR scale detect: {randr.Length} output(s), {xinerama.Length} Xinerama, desktopAuto={GetDesktopAutoScaleFrom(geometry):F3}");
        foreach (var o in randr)
        {
            AppLog.Info(
                $"XRandR: {o.Name} CRTC {o.CurrentWidth}x{o.CurrentHeight}+{o.X}+{o.Y} "
                + $"phys={o.PhysicalWidth}x{o.PhysicalHeight} rot=0x{o.Rotation:x} "
                + $"X11Scale={o.X11Scale:F3}");
        }
        foreach (var g in geometry)
        {
            AppLog.Info(
                $"XRandR map: [{g.Index}] {g.Name}"
                + (string.IsNullOrEmpty(g.Description) ? "" : $" ({g.Description})")
                + $" logical {g.LogicalWidth}x{g.LogicalHeight} "
                + $"physical {g.PhysicalWidth}x{g.PhysicalHeight} "
                + $"scale={g.X11Scale:F3} recommended {g.RecommendedWidth}x{g.RecommendedHeight}");
        }
    }

    private static double GetDesktopAutoScaleFrom(MonitorGeometry[] geometry)
    {
        return geometry.Where(g => g.X11Scale > 0).Min(g => g.X11Scale);
    }
}
