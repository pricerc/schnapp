namespace Schnapp.Client.Linux.Capture;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Schnapp.Client.Linux.Services;

using SixLabors.ImageSharp;

/// <summary>
/// Fallback capture via external tools (ImageMagick <c>import</c>, <c>scrot</c>, <c>gnome-screenshot</c>).
/// Only full-desktop; loads tool output into ImageSharp for the shared encode path.
/// </summary>
public sealed class ImageMagickScreenCapture : IScreenCapture
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public int GetMonitorCount() => 1;

    /// <inheritdoc />
    public string GetMonitorName(int monitorIndex) =>
        monitorIndex == 0 ? "primary" : $"Monitor {monitorIndex + 1}";

    /// <inheritdoc />
    public double GetMonitorDetectedScale(int monitorIndex) => 1.0;

    /// <inheritdoc />
    public MonitorGeometry GetMonitorGeometry(int monitorIndex) => new()
    {
        Index = monitorIndex,
        Name = GetMonitorName(monitorIndex),
        LogicalWidth = 1920,
        LogicalHeight = 1080,
        PhysicalWidth = 1920,
        PhysicalHeight = 1080
    };

    /// <inheritdoc />
    public double GetDesktopAutoScale() => 1.0;

    /// <inheritdoc />
    /// <remarks>Only full-desktop via external tools; window/monitor flags are ignored.</remarks>
    public async Task<IReadOnlyList<ScreenCaptureResult>> CaptureAsync(
        bool captureWindow,
        bool captureMonitors,
        bool captureFullDesktop,
        IReadOnlyList<int>? monitorIndexes = null,
        CancellationToken ct = default)
    {
        AppLog.Debug("IM: CaptureAsync (fallback) enter");
        _ = captureWindow;
        _ = captureMonitors;
        _ = captureFullDesktop;
        _ = monitorIndexes;

        var png = await CapturePngAsync(ct).ConfigureAwait(false);
        if (png == null || png.Length == 0)
        {
            AppLog.Warn("IM: CapturePngAsync returned empty");
            return Array.Empty<ScreenCaptureResult>();
        }

        try
        {
            using var ms = new MemoryStream(png);
            var image = await Image.LoadAsync(ms, ct).ConfigureAwait(false);
            AppLog.Debug($"IM: loaded external capture {image.Width}x{image.Height}");
            return new[]
            {
                new ScreenCaptureResult
                {
                    Image = image,
                    CaptureType = "Full",
                    MonitorName = "desktop",
                    Description = "Full desktop (external tool)"
                }
            };
        }
        catch (Exception ex)
        {
            AppLog.Error("IM: load failed: " + ex.Message);
            return Array.Empty<ScreenCaptureResult>();
        }
    }

    private static async Task<byte[]?> CapturePngAsync(CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "schnapp-cap-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            AppLog.Debug($"IM: tmp={tmp}");
            bool ok = await TryRunAsync("import", $"-silent -window root \"{tmp}\"", ct).ConfigureAwait(false);
            if (!ok)
            {
                ok = await TryRunAsync("scrot", $"-o \"{tmp}\"", ct).ConfigureAwait(false);
            }

            if (!ok)
            {
                ok = await TryRunAsync("gnome-screenshot", $"-f \"{tmp}\"", ct).ConfigureAwait(false);
            }

            if (ok && File.Exists(tmp))
            {
                var bytes = await File.ReadAllBytesAsync(tmp, ct).ConfigureAwait(false);
                AppLog.Debug($"IM: external tool produced {bytes.Length} bytes");
                return bytes;
            }
            AppLog.Warn("IM: no tool succeeded or file missing");
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task<bool> TryRunAsync(string fileName, string args, CancellationToken ct)
    {
        try
        {
            AppLog.Debug($"IM: spawn {fileName} {args}");
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                AppLog.Debug($"IM: failed to start {fileName}");
                return false;
            }
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            var exit = p.ExitCode;
            var err = (await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false)).Trim();
            AppLog.Debug($"IM: {fileName} exit={exit} err='{err}'");
            return exit == 0;
        }
        catch (Exception ex)
        {
            AppLog.Debug($"IM: {fileName} threw: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// Tries native X11 first; falls back to external screenshot tools.
/// </summary>
public sealed class CompositeScreenCapture : IScreenCapture, IDisposable
{
    private readonly IScreenInfoProvider _screenInfo;
    private readonly X11ScreenCapture? _x11;
    private readonly ImageMagickScreenCapture _fallback = new();

    /// <summary>
    /// Creates a composite backend using <paramref name="screenInfo"/> for geometry
    /// and preferring <see cref="X11ScreenCapture"/> when the display is available.
    /// </summary>
    public CompositeScreenCapture(IScreenInfoProvider screenInfo)
    {
        _screenInfo = screenInfo;
        try
        {
            var x11 = new X11ScreenCapture(screenInfo);
            if (x11.IsAvailable)
            {
                _x11 = x11;
                AppLog.Debug("Composite: using X11 backend");
            }
            else
            {
                x11.Dispose();
                AppLog.Debug("Composite: X11 not available, will fallback");
            }
        }
        catch (Exception ex)
        {
            _x11 = null;
            AppLog.Debug("Composite: X11 ctor threw: " + ex.Message);
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => _x11?.IsAvailable == true || _fallback.IsAvailable;

    /// <inheritdoc />
    public int GetMonitorCount() => _x11?.GetMonitorCount() ?? _screenInfo.GetMonitorCount();

    /// <inheritdoc />
    public string GetMonitorName(int monitorIndex) =>
        _x11?.GetMonitorName(monitorIndex) ?? _screenInfo.GetMonitorName(monitorIndex);

    /// <inheritdoc />
    public double GetMonitorDetectedScale(int monitorIndex) =>
        _x11?.GetMonitorDetectedScale(monitorIndex) ?? _screenInfo.GetMonitorDetectedScale(monitorIndex);

    /// <inheritdoc />
    public MonitorGeometry GetMonitorGeometry(int monitorIndex) =>
        _x11?.GetMonitorGeometry(monitorIndex) ?? _screenInfo.GetMonitorGeometry(monitorIndex);

    /// <inheritdoc />
    public double GetDesktopAutoScale() =>
        _x11?.GetDesktopAutoScale() ?? _screenInfo.GetDesktopAutoScale();

    /// <inheritdoc />
    public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAsync(
        bool captureWindow,
        bool captureMonitors,
        bool captureFullDesktop,
        IReadOnlyList<int>? monitorIndexes = null,
        CancellationToken ct = default)
    {
        if (_x11 is { IsAvailable: true })
        {
            AppLog.Debug("Composite: delegate to X11");
            return _x11.CaptureAsync(captureWindow, captureMonitors, captureFullDesktop, monitorIndexes, ct);
        }
        AppLog.Debug("Composite: delegate to fallback (IM)");
        return _fallback.CaptureAsync(captureWindow, captureMonitors, captureFullDesktop, monitorIndexes, ct);
    }

    /// <summary>Disposes the X11 backend when present.</summary>
    public void Dispose() => _x11?.Dispose();
}
