namespace Schnapp.Client.Linux.Capture;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Schnapp.Client.Linux.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// In-process X11 capture via shared <see cref="X11Native"/> (no external screenshot tools).
/// Two-phase bulk capture: sequential raw <c>XGetImage</c>, then parallel BGRA→RGBA + PNG encode.
/// </summary>
public sealed class X11ScreenCapture : IScreenCapture, IDisposable
{
    private static readonly TimeSpan MinCaptureInterval = TimeSpan.FromSeconds(1);

    private readonly IScreenInfoProvider _screenInfo;
    private readonly SemaphoreSlim _throttleGate = new(1, 1);
    private DateTime _lastCaptureStartUtc = DateTime.MinValue;
    private bool _disposed;

    private sealed class RawXImageCapture
    {
        public IntPtr XImagePtr { get; set; }
        public X11Native.XImage XImageStruct { get; set; }
        public string CaptureType { get; set; } = "Full";
        public int? MonitorIndex { get; set; }
        public string? MonitorName { get; set; }
        public string? WindowTitle { get; set; }
        public string? ProgramName { get; set; }
        public string Description { get; set; } = "Screenshot";
        public Dictionary<string, string> Metadata { get; set; } = [];
    }

    /// <summary>
    /// Creates a capture backend that uses <paramref name="screenInfo"/> for monitor layout.
    /// </summary>
    public X11ScreenCapture(IScreenInfoProvider screenInfo)
    {
        _screenInfo = screenInfo;
        AppLog.Debug($"X11ScreenCapture: ctor, X11 available={X11Native.IsAvailable}, monitorCount={_screenInfo.GetMonitorCount()}");
    }

    /// <inheritdoc />
    public bool IsAvailable => X11Native.IsAvailable;

    /// <inheritdoc />
    public int GetMonitorCount() => _screenInfo.GetMonitorCount();

    /// <inheritdoc />
    public string GetMonitorName(int monitorIndex) => _screenInfo.GetMonitorName(monitorIndex);

    /// <inheritdoc />
    public double GetMonitorDetectedScale(int monitorIndex) =>
        _screenInfo.GetMonitorDetectedScale(monitorIndex);

    /// <inheritdoc />
    public MonitorGeometry GetMonitorGeometry(int monitorIndex) =>
        _screenInfo.GetMonitorGeometry(monitorIndex);

    /// <inheritdoc />
    public double GetDesktopAutoScale() => _screenInfo.GetDesktopAutoScale();

    /// <summary>Releases the capture throttle gate.</summary>
    public void Dispose()
    {
        _disposed = true;
        _throttleGate.Dispose();
        AppLog.Debug("X11ScreenCapture: disposed");
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Captures any combination of active window, monitors, and full desktop.
    /// </summary>
    /// <remarks>
    /// Throttled to run no more than once per second: concurrent/rapid calls are serialized via
    /// <see cref="_throttleGate"/> and, if the previous capture started less than
    /// <see cref="MinCaptureInterval"/> ago, this call waits out the remainder before capturing.
    /// </remarks>
    public async Task<IReadOnlyList<ScreenCaptureResult>> CaptureAsync(
        bool captureWindow,
        bool captureMonitors,
        bool captureFullDesktop,
        IReadOnlyList<int>? monitorIndexes = null,
        CancellationToken ct = default)
    {
        await _throttleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var elapsed = DateTime.UtcNow - _lastCaptureStartUtc;
            if (elapsed < MinCaptureInterval)
            {
                var wait = MinCaptureInterval - elapsed;
                AppLog.Debug($"X11: throttling CaptureAsync, waiting {wait.TotalMilliseconds:F0}ms");
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
            _lastCaptureStartUtc = DateTime.UtcNow;

            return await CaptureCoreAsync(captureWindow, captureMonitors, captureFullDesktop, monitorIndexes, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _throttleGate.Release();
        }
    }

    private async Task<IReadOnlyList<ScreenCaptureResult>> CaptureCoreAsync(
        bool captureWindow,
        bool captureMonitors,
        bool captureFullDesktop,
        IReadOnlyList<int>? monitorIndexes,
        CancellationToken ct)
    {
        AppLog.Debug("X11: CaptureAsync enter");
        if (_disposed)
        {
            AppLog.Debug("X11: disposed -> empty");
            return Array.Empty<ScreenCaptureResult>();
        }
        if (!X11Native.IsAvailable)
        {
            AppLog.Warn("X11: not available (display open failed)");
            return Array.Empty<ScreenCaptureResult>();
        }

        if (!captureWindow && !captureMonitors && !captureFullDesktop)
        {
            captureMonitors = true;
        }


        AppLog.Debug($"X11: plan w={captureWindow} m={captureMonitors} f={captureFullDesktop} idx=[{string.Join(",", monitorIndexes ?? Array.Empty<int>())}]");

        var display = X11Native.Display;
        var rawCaptures = new List<(string type, RawXImageCapture raw)>();

        // Window first (often most relevant), then monitors, then full desktop.
        if (captureWindow)
        {
            CaptureActiveWindowRaw(display, rawCaptures, ct);
        }


        if (captureMonitors)
        {
            var indexes = ResolveMonitorIndexes(monitorIndexes);
            CaptureMonitorsRaw(display, indexes, rawCaptures, ct);
        }

        if (captureFullDesktop)
        {
            CaptureFullDesktopRaw(display, rawCaptures, ct);
        }


        if (rawCaptures.Count == 0)
        {
            AppLog.Debug("X11: no raw captures produced");
            return Array.Empty<ScreenCaptureResult>();
        }

        AppLog.Debug($"X11: processing {rawCaptures.Count} raw frames");
        var tasks = rawCaptures.Select(async capture =>
        {
            var (t, raw) = capture;
            try
            {
                var image = await Task.Run(() => ProcessRawToImage(raw), ct).ConfigureAwait(false);
                if (image == null)
                {
                    AppLog.Warn($"X11: ProcessRawToImage returned null for {t}");
                    return null;
                }
                AppLog.Debug($"X11: processed {t} -> {image.Width}x{image.Height}");
                return new ScreenCaptureResult
                {
                    Image = image,
                    CaptureType = raw.CaptureType,
                    MonitorIndex = raw.MonitorIndex,
                    MonitorName = raw.MonitorName,
                    WindowTitle = raw.WindowTitle,
                    ProgramName = raw.ProgramName,
                    Description = raw.Description,
                    Metadata = raw.Metadata
                };
            }
            finally
            {
                lock (X11Native.Sync)
                {
                    if (raw.XImagePtr != IntPtr.Zero)
                    {
                        int v = X11Native.XDestroyImage(raw.XImagePtr);
                        raw.XImagePtr = IntPtr.Zero;
                    }
                }
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var list = results.Where(r => r != null).Cast<ScreenCaptureResult>().ToList();
        AppLog.Debug($"X11: CaptureAsync -> {list.Count} results");
        return list;
    }

    /// <summary>
    /// Capture the currently active window and add its raw capture to the provided list.
    /// </summary>
    /// <param name="display">The X11 display pointer.</param>
    /// <param name="rawCaptures">The list to add the raw capture to.</param>
    /// <param name="ct">The cancellation token.</param>
    private List<int> ResolveMonitorIndexes(IReadOnlyList<int>? monitorIndexes)
    {
        var count = Math.Max(0, _screenInfo.GetMonitorCount());
        if (monitorIndexes == null || monitorIndexes.Count == 0)
        {

            return [.. Enumerable.Range(0, count)];
        }


        return [.. monitorIndexes
            .Where(i => i >= 0 && i < count)
            .Distinct()
            .OrderBy(i => i)];
    }

    private void CaptureActiveWindowRaw(
        IntPtr display,
        List<(string type, RawXImageCapture raw)> rawCaptures,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var windowId = GetActiveWindow(display);
        if (windowId == IntPtr.Zero)
        {
            AppLog.Debug("X11: active window = 0 (none)");
            return;
        }

        AppLog.Debug($"X11: active window=0x{windowId.ToInt64():x}");
        var raw = CaptureDrawableRaw(display, windowId, 0, 0, null, null);
        if (raw == null)
        {
            AppLog.Warn("X11: CaptureDrawableRaw failed for active window");
            return;
        }

        var (name, className) = GetWindowInfo(display, windowId);
        raw.CaptureType = "Window";
        raw.WindowTitle = name;
        raw.ProgramName = className;
        raw.Description = name ?? "Active window";
        raw.Metadata["WindowId"] = $"0x{windowId.ToInt64():x}";
        if (!string.IsNullOrEmpty(name))
        {
            raw.Metadata["WindowTitle"] = name;
        }


        if (!string.IsNullOrEmpty(className))
        {
            raw.Metadata["WindowClass"] = className;
        }

        // Map window center to a monitor so encode can apply per-monitor scale/quality.

        if (TryGetWindowRootCenter(display, windowId, out var cx, out var cy))
        {
            var mon = _screenInfo.FindMonitorAt(cx, cy);
            if (mon is int mi)
            {
                raw.MonitorIndex = mi;
                raw.MonitorName = _screenInfo.GetMonitorName(mi);
                raw.Metadata["MonitorIndex"] = mi.ToString();
                AppLog.Debug($"X11: window center ({cx},{cy}) -> monitor {mi}");
            }
        }

        AppLog.Debug($"X11: window info title='{name}' class='{className}'");
        rawCaptures.Add(("Window", raw));
    }

    /// <summary>
    /// Translates the window client center into root coordinates.
    /// </summary>
    private static bool TryGetWindowRootCenter(IntPtr display, IntPtr windowId, out int rootX, out int rootY)
    {
        rootX = 0;
        rootY = 0;
        lock (X11Native.Sync)
        {
            try
            {
                var attrs = new X11Native.XWindowAttributes();
                if (X11Native.XGetWindowAttributes(display, windowId, ref attrs) == 0)
                {
                    return false;
                }


                var cx = attrs.width / 2;
                var cy = attrs.height / 2;
                var root = X11Native.DefaultRootWindow(display);
                if (X11Native.XTranslateCoordinates(
                        display, windowId, root, cx, cy, out rootX, out rootY, out _) == 0)
                {

                    return false;
                }


                return true;
            }
            catch (Exception ex)
            {
                AppLog.Debug("X11: TryGetWindowRootCenter: " + ex.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Capture the full desktop and add its raw capture to the provided list.
    /// </summary>
    /// <param name="display">The X11 display pointer.</param>
    /// <param name="rawCaptures">The list to add the raw capture to.</param>
    /// <param name="ct">The cancellation token.</param>
    private static void CaptureFullDesktopRaw(
        IntPtr display,
        List<(string type, RawXImageCapture raw)> rawCaptures,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IntPtr root;
        lock (X11Native.Sync)
        {
            root = X11Native.DefaultRootWindow(display);
        }


        AppLog.Debug($"X11: full desktop root=0x{root.ToInt64():x}");
        var raw = CaptureDrawableRaw(display, root, 0, 0, null, null);
        if (raw == null)
        {
            AppLog.Warn("X11: CaptureDrawableRaw failed for full desktop");
            return;
        }

        raw.CaptureType = "Full";
        raw.MonitorName = "desktop";
        raw.Description = "Full desktop";
        raw.Metadata["CaptureType"] = "FullDesktop";
        rawCaptures.Add(("Full", raw));
    }

    /// <summary>
    /// Capture the specified monitors and add their raw captures to the provided list.
    /// </summary>
    /// <param name="display">The X11 display pointer.</param>
    /// <param name="indexes">The list of monitor indexes to capture.</param>
    /// <param name="rawCaptures">The list to add the raw captures to.</param>
    /// <param name="ct">The cancellation token.</param>
    private void CaptureMonitorsRaw(
        IntPtr display,
        List<int> indexes,
        List<(string type, RawXImageCapture raw)> rawCaptures,
        CancellationToken ct)
    {
        if (indexes.Count == 0)
        {
            AppLog.Debug("X11: no monitor indexes to capture");
            return;
        }

        IntPtr root;
        lock (X11Native.Sync)
        {
            root = X11Native.DefaultRootWindow(display);
        }


        var monitorCount = _screenInfo.GetMonitorCount();
        AppLog.Debug($"X11: capturing {indexes.Count} monitors (reported count={monitorCount})");
        foreach (var i in indexes)
        {
            ct.ThrowIfCancellationRequested();
            if (i < 0 || i >= monitorCount)
            {
                AppLog.Debug($"X11: skip monitor {i} (out of range)");
                continue;
            }

            var bounds = _screenInfo.GetMonitorBounds(i);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                AppLog.Debug($"X11: skip monitor {i} (bad bounds {bounds.Width}x{bounds.Height})");
                continue;
            }

            AppLog.Debug($"X11: monitor {i} @ ({bounds.X},{bounds.Y}) {bounds.Width}x{bounds.Height}");
            var raw = CaptureDrawableRaw(
                display,
                root,
                bounds.X,
                bounds.Y,
                (uint)bounds.Width,
                (uint)bounds.Height);
            if (raw == null)
            {
                AppLog.Warn($"X11: CaptureDrawableRaw failed for monitor {i}");
                continue;
            }

            raw.CaptureType = "Monitor";
            raw.MonitorIndex = i;
            raw.MonitorName = _screenInfo.GetMonitorName(i);
            raw.Description = raw.MonitorName;
            raw.Metadata["CaptureType"] = "Monitor";
            raw.Metadata["MonitorIndex"] = i.ToString();
            rawCaptures.Add(($"Monitor-{i}", raw));
        }
    }

    /// <summary>
    /// Get the currently active X11 window for the specified display.
    /// If no active window is found, returns <see cref="IntPtr.Zero"/>.
    /// </summary>
    /// <param name="display">The X11 display pointer.</param>
    /// <returns>The handle of the currently active X11 window, or <see cref="IntPtr.Zero"/> if none is active.</returns>
    private static IntPtr GetActiveWindow(IntPtr display)
    {
        if (display == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }


        lock (X11Native.Sync)
        {
            try
            {
                var rootWindow = X11Native.DefaultRootWindow(display);
                var atom = X11Native.XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
                IntPtr prop = IntPtr.Zero;
                var result = X11Native.XGetWindowProperty(
                    display,
                    rootWindow,
                    atom,
                    0,
                    1,
                    false,
                    X11Native.XA_WINDOW,
                    out _,
                    out _,
                    out var nItems,
                    out _,
                    ref prop);

                AppLog.Debug($"X11: _NET_ACTIVE_WINDOW atom={atom:x} result={result} nItems={nItems}");

                if (result == 0 && prop != IntPtr.Zero && nItems > 0)
                {
                    var windowId = Marshal.ReadIntPtr(prop);
                    _ = X11Native.XFree(prop);
                    AppLog.Debug($"X11: active window from prop=0x{windowId.ToInt64():x}");
                    return windowId;
                }

                if (prop != IntPtr.Zero)
                {
                    _ = X11Native.XFree(prop);
                }

            }
            catch (Exception ex)
            {
                AppLog.Debug("X11: GetActiveWindow threw: " + ex.Message);
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Get the window title and class name for the specified X11 window.
    /// </summary>
    /// <param name="display">The X11 display pointer.</param>
    /// <param name="window">The X11 window handle.</param>
    /// <returns>A tuple containing the window title and class name, or null if unavailable.</returns>
    private static (string? name, string? className) GetWindowInfo(IntPtr display, IntPtr window)
    {
        if (display == IntPtr.Zero || window == IntPtr.Zero)
        {

            return (null, null);
        }


        lock (X11Native.Sync)
        {
            try
            {
                IntPtr namePtr = IntPtr.Zero;
                X11Native.XFetchName(display, window, ref namePtr);
                string? name = namePtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(namePtr) : null;
                if (namePtr != IntPtr.Zero)
                {
                    _ = X11Native.XFree(namePtr);
                }


                var classHint = new X11Native.XClassHint();
                if (X11Native.XGetClassHint(display, window, ref classHint) != 0)
                {
                    string? className = classHint.res_class != IntPtr.Zero
                        ? Marshal.PtrToStringAnsi(classHint.res_class)
                        : null;
                    if (classHint.res_name != IntPtr.Zero)
                    {
                        _ = X11Native.XFree(classHint.res_name);
                    }


                    if (classHint.res_class != IntPtr.Zero)
                    {
                        _ = X11Native.XFree(classHint.res_class);
                    }


                    AppLog.Debug($"X11: GetWindowInfo title='{name}' class='{className}'");
                    return (name, className);
                }

                AppLog.Debug($"X11: GetWindowInfo title='{name}' (no class hint)");
                return (name, null);
            }
            catch (Exception ex)
            {
                AppLog.Debug("X11: GetWindowInfo threw: " + ex.Message);
                return (null, null);
            }
        }
    }

    /// <summary>
    /// Capture a raw image of the specified drawable area.
    /// </summary>
    /// <param name="display">The X11 display pointer.</param>
    /// <param name="drawable">The drawable (window or pixmap) to capture.</param>
    /// <param name="x">The X coordinate of the top-left corner of the capture area.</param>
    /// <param name="y">The Y coordinate of the top-left corner of the capture area.</param>
    /// <param name="width">The width of the capture area. If null, the drawable's width is used.</param>
    /// <param name="height">The height of the capture area. If null, the drawable's height is used.</param>
    /// <returns>A raw image capture of the specified area, or null if the capture failed.</returns>
    private static RawXImageCapture? CaptureDrawableRaw(
        IntPtr display,
        IntPtr drawable,
        int x,
        int y,
        uint? width,
        uint? height)
    {
        if (display == IntPtr.Zero || drawable == IntPtr.Zero)
        {
            AppLog.Debug("X11: CaptureDrawableRaw bad display/drawable");
            return null;
        }

        lock (X11Native.Sync)
        {
            try
            {
                uint w, h;
                if (width is null || height is null)
                {
                    var attrs = new X11Native.XWindowAttributes();
                    if (X11Native.XGetWindowAttributes(display, drawable, ref attrs) == 0)
                    {
                        AppLog.Debug("X11: XGetWindowAttributes failed");
                        return null;
                    }
                    w = (uint)attrs.width;
                    h = (uint)attrs.height;
                    AppLog.Debug($"X11: attrs {w}x{h} @ {attrs.x},{attrs.y}");
                }
                else
                {
                    w = width.Value;
                    h = height.Value;
                }

                if (w == 0 || h == 0)
                {
                    AppLog.Debug("X11: zero size drawable");
                    return null;
                }

                AppLog.Debug($"X11: XGetImage {w}x{h} at ({x},{y})");
                var xImagePtr = X11Native.XGetImage(
                    display,
                    drawable,
                    x,
                    y,
                    w,
                    h,
                    X11Native.AllPlanes,
                    X11Native.ZPixmap);

                if (xImagePtr == IntPtr.Zero)
                {
                    AppLog.Warn("X11: XGetImage returned null");
                    return null;
                }

                var xImage = Marshal.PtrToStructure<X11Native.XImage>(xImagePtr);
                AppLog.Debug($"X11: XImage {xImage.width}x{xImage.height} bpp={xImage.bits_per_pixel} bpl={xImage.bytes_per_line} masks r={xImage.red_mask:x} g={xImage.green_mask:x} b={xImage.blue_mask:x}");
                return new RawXImageCapture
                {
                    XImagePtr = xImagePtr,
                    XImageStruct = xImage
                };
            }
            catch (Exception ex)
            {
                AppLog.Debug("X11: CaptureDrawableRaw threw: " + ex.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// Copy XImage pixels into an ImageSharp <see cref="Image{Bgra32}"/> or <see cref="Image{Rgba32}"/>
    /// with no intermediate codec (row-aware for <c>bytes_per_line</c> padding).
    /// </summary>
    private static Image? ProcessRawToImage(RawXImageCapture raw)
    {
        try
        {
            var xImage = raw.XImageStruct;
            AppLog.Debug($"X11: ProcessRawToImage {xImage.width}x{xImage.height} bpp={xImage.bits_per_pixel} bpl={xImage.bytes_per_line} data=0x{xImage.data:x}");
            if (xImage.width <= 0 || xImage.height <= 0 || xImage.data == IntPtr.Zero)
            {
                AppLog.Warn("X11: ProcessRawToImage bad dimensions or null data");
                return null;
            }

            var bytesPerPixel = xImage.bits_per_pixel / 8;

            // Fast path: standard 32-bit BGRA → Image<Bgra32> (native X11 layout on little-endian)
            if (bytesPerPixel == 4
                && xImage.red_mask == 0xFF0000
                && xImage.green_mask == 0xFF00
                && xImage.blue_mask == 0xFF)
            {
                AppLog.Debug("X11: fast BGRA32 path");
                var image = new Image<Bgra32>(xImage.width, xImage.height);
                unsafe
                {
                    var dataPtr = (byte*)xImage.data;
                    image.ProcessPixelRows(accessor =>
                    {
                        for (var y = 0; y < accessor.Height; y++)
                        {
                            var dest = accessor.GetRowSpan(y);
                            var src = (Bgra32*)(dataPtr + (y * xImage.bytes_per_line));
                            for (var x = 0; x < dest.Length; x++)
                            {

                                dest[x] = src[x];
                            }

                        }
                    });
                }
                return image;
            }

            // Generic path → Rgba32
            AppLog.Debug("X11: generic pixel path");
            var rShift = GetMaskShift(xImage.red_mask);
            var gShift = GetMaskShift(xImage.green_mask);
            var bShift = GetMaskShift(xImage.blue_mask);
            var rBits = GetMaskBits(xImage.red_mask);
            var gBits = GetMaskBits(xImage.green_mask);
            var bBits = GetMaskBits(xImage.blue_mask);
            AppLog.Debug($"X11: mask shifts r={rShift} g={gShift} b={bShift} bits r={rBits} g={gBits} b={bBits}");

            var rgba = new Image<Rgba32>(xImage.width, xImage.height);
            unsafe
            {
                var dataPtr = (byte*)xImage.data;
                for (var row = 0; row < xImage.height; row++)
                {
                    var rowPtr = dataPtr + (row * xImage.bytes_per_line);
                    for (var col = 0; col < xImage.width; col++)
                    {
                        var pixelPtr = rowPtr + (col * Math.Max(1, bytesPerPixel));
                        ulong pixel;
                        if (bytesPerPixel == 4)
                        {
                            pixel = *(uint*)pixelPtr;
                        }

                        else if (bytesPerPixel == 3)
                        {
                            pixel = (ulong)(pixelPtr[0] | (pixelPtr[1] << 8) | (pixelPtr[2] << 16));
                        }

                        else if (bytesPerPixel == 2)
                        {
                            pixel = *(ushort*)pixelPtr;
                        }
                        else
                        {
                            pixel = pixelPtr[0];
                        }


                        var r = ScaleColorComponent((pixel >> rShift) & ((1UL << rBits) - 1), rBits);
                        var g = ScaleColorComponent((pixel >> gShift) & ((1UL << gBits) - 1), gBits);
                        var b = ScaleColorComponent((pixel >> bShift) & ((1UL << bBits) - 1), bBits);
                        rgba[col, row] = new Rgba32(r, g, b, 255);
                    }
                }
            }
            return rgba;
        }
        catch (Exception ex)
        {
            AppLog.Error("X11: ProcessRawToImage threw: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Get the number of bits to shift for the specified color mask.
    /// </summary>
    /// <param name="mask">The color mask.</param>
    /// <returns>The number of bits to shift for the specified color mask.</returns>
    private static int GetMaskShift(ulong mask)
    {
        if (mask == 0)
        {
            return 0;
        }


        var shift = 0;
        while ((mask & 1) == 0 && shift < 64)
        {
            mask >>= 1;
            shift++;
        }
        return shift;
    }

    /// <summary>
    /// Get the number of bits set in the specified color mask.
    /// </summary>
    /// <param name="mask">The color mask.</param>
    /// <returns>The number of bits set in the specified color mask.</returns>
    private static int GetMaskBits(ulong mask)
    {
        if (mask == 0)
        {
            return 0;
        }


        while ((mask & 1) == 0)
        {
            mask >>= 1;
        }


        var bits = 0;
        while ((mask & 1) == 1 && bits < 64)
        {
            mask >>= 1;
            bits++;
        }
        return bits;
    }

    /// <summary>
    /// Scale a color component value from its original bit depth to 8 bits.
    /// </summary>
    /// <param name="value">The color component value.</param>
    /// <param name="bits">The original bit depth of the color component.</param>
    /// <returns>The color component value scaled to 8 bits.</returns>
    private static byte ScaleColorComponent(ulong value, int bits)
    {
        if (bits == 8)
        {
            return (byte)value;
        }


        if (bits < 8)
        {

            return (byte)(value << (8 - bits));
        }


        return (byte)(value >> (bits - 8));
    }
}
