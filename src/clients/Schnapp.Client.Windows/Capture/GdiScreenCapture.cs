namespace Schnapp.Client.Windows.Capture;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Schnapp.Client.Windows.Services;

/// <summary>
/// GDI screen capture using <see cref="Graphics.CopyFromScreen"/> and optional PrintWindow for active windows.
/// </summary>
/// <remarks>
/// Called by <see cref="Services.CaptureService"/> on a background thread. Prefer this over DXGI for MVP
/// reliability (RDP-friendly). Dispose is a no-op (no native display connection to close).
/// </remarks>
public sealed class GdiScreenCapture : IScreenCapture, IDisposable
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public int GetMonitorCount() => Screen.AllScreens.Length;

    /// <inheritdoc />
    public string GetMonitorName(int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
        {
            return "Monitor " + monitorIndex;
        }

        var s = screens[monitorIndex];
        var label = s.Primary ? "Primary" : "Monitor " + (monitorIndex + 1);
        return label + " (" + s.Bounds.Width + "x" + s.Bounds.Height + ")";
    }

    /// <inheritdoc />
    /// <remarks>Windows GDI path never auto-shrinks; user chooses scale in Settings.</remarks>
    public double GetMonitorDetectedScale(int monitorIndex) => 1.0;

    /// <inheritdoc />
    public MonitorGeometry GetMonitorGeometry(int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorIndex));
        }

        var s = screens[monitorIndex];
        var b = s.Bounds;

        // Because scale is always 1 on Windows — no Xlibre CRTC / DPI auto-shrink.
        // Output size is chosen by the user via MonitorScaling steps in Settings.
        return new MonitorGeometry
        {
            Index = monitorIndex,
            Name = s.DeviceName ?? ("DISPLAY" + monitorIndex),
            Description = GetMonitorName(monitorIndex),
            LogicalX = b.X,
            LogicalY = b.Y,
            LogicalWidth = b.Width,
            LogicalHeight = b.Height,
            PhysicalWidth = b.Width,
            PhysicalHeight = b.Height,
            OsScale = 1.0,
            DetectedScale = 1.0
        };
    }

    /// <inheritdoc />
    public double GetDesktopAutoScale() => 1.0;

    /// <inheritdoc />
    public long? TryGetFocusedWindowId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsOwnProcess(hwnd) || IsShellWindow(hwnd))
        {
            return null;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        // Because menus, tooltips, and thumbnail previews are not windows worth a snapshot.
        if (width < 32 || height < 32)
        {
            return null;
        }

        return hwnd.ToInt64();
    }

    /// <summary>True when this process owns <paramref name="hwnd"/>.</summary>
    private static bool IsOwnProcess(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && pid == (uint)Process.GetCurrentProcess().Id;
    }

    /// <summary>True for the desktop and taskbar, which are not useful focus snapshots.</summary>
    private static bool IsShellWindow(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        if (GetClassName(hwnd, sb, sb.Capacity) <= 0)
        {
            return false;
        }

        var name = sb.ToString();
        return name == "Progman"
            || name == "WorkerW"
            || name == "Shell_TrayWnd"
            || name == "Shell_SecondaryTrayWnd";
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAsync(
        bool captureWindow,
        bool captureMonitors,
        bool captureFullDesktop,
        IReadOnlyList<int>? monitorIndexes = null,
        CancellationToken ct = default)
    {
        // Because GDI calls must not block the UI thread; Task.Run keeps CaptureService free.
        return Task.Run(() => CaptureCore(captureWindow, captureMonitors, captureFullDesktop, monitorIndexes, ct), ct);
    }

    /// <summary>
    /// Synchronous capture of the requested targets (window, monitors, full desktop).
    /// </summary>
    /// <param name="captureWindow">When true, capture the foreground window.</param>
    /// <param name="captureMonitors">When true, capture per-monitor images.</param>
    /// <param name="captureFullDesktop">When true, capture the virtual desktop bounds.</param>
    /// <param name="monitorIndexes">Subset of monitors, or null/empty for all.</param>
    /// <param name="ct">Cancellation checked between targets.</param>
    /// <returns>Successfully grabbed frames (missing window is omitted, not an error).</returns>
    private IReadOnlyList<ScreenCaptureResult> CaptureCore(
        bool captureWindow,
        bool captureMonitors,
        bool captureFullDesktop,
        IReadOnlyList<int>? monitorIndexes,
        CancellationToken ct)
    {
        var list = new List<ScreenCaptureResult>();

        if (captureWindow)
        {
            ct.ThrowIfCancellationRequested();
            var win = CaptureActiveWindow();
            if (win != null)
            {
                list.Add(win);
            }
        }

        if (captureMonitors)
        {
            var screens = Screen.AllScreens;
            IEnumerable<int> indexes;
            if (monitorIndexes == null || monitorIndexes.Count == 0)
            {
                var all = new List<int>();
                for (var i = 0; i < screens.Length; i++)
                {
                    all.Add(i);
                }

                indexes = all;
            }
            else
            {
                indexes = monitorIndexes;
            }

            foreach (var i in indexes)
            {
                ct.ThrowIfCancellationRequested();
                if (i < 0 || i >= screens.Length)
                {
                    continue;
                }

                list.Add(CaptureMonitor(i, screens[i]));
            }
        }

        if (captureFullDesktop)
        {
            ct.ThrowIfCancellationRequested();
            list.Add(CaptureFullDesktop());
        }

        return list;
    }

    /// <summary>
    /// Grabs a single monitor's bounds via <see cref="GrabRegion"/>.
    /// </summary>
    /// <param name="index">Zero-based monitor index for metadata.</param>
    /// <param name="screen">WinForms <see cref="Screen"/> for bounds.</param>
    /// <returns>Monitor capture result owning a new bitmap.</returns>
    private ScreenCaptureResult CaptureMonitor(int index, Screen screen)
    {
        var b = screen.Bounds;
        var bmp = GrabRegion(b.X, b.Y, b.Width, b.Height);
        return new ScreenCaptureResult
        {
            Image = bmp,
            CaptureType = "Monitor",
            MonitorIndex = index,
            MonitorName = GetMonitorName(index),
            Description = "Monitor " + index
        };
    }

    /// <summary>
    /// Grabs the full virtual desktop (all monitors stitched by coordinate space).
    /// </summary>
    /// <returns>Full-desktop capture result.</returns>
    private static ScreenCaptureResult CaptureFullDesktop()
    {
        var vs = SystemInformation.VirtualScreen;
        var bmp = GrabRegion(vs.X, vs.Y, vs.Width, vs.Height);
        return new ScreenCaptureResult
        {
            Image = bmp,
            CaptureType = "Full",
            Description = "Full desktop",
            MonitorName = "desktop"
        };
    }

    /// <summary>
    /// Captures the foreground window via PrintWindow, falling back to CopyFromScreen.
    /// </summary>
    /// <returns>Window result, or <c>null</c> when there is no usable foreground window.</returns>
    private ScreenCaptureResult? CaptureActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        Bitmap? bmp = null;
        try
        {
            bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                try
                {
                    // Because PrintWindow handles some layered/DWM windows better than CopyFromScreen.
                    if (!PrintWindow(hwnd, hdc, 2 /* PW_RENDERFULLCONTENT */))
                    {
                        g.ReleaseHdc(hdc);
                        hdc = IntPtr.Zero;
                        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                    }
                }
                finally
                {
                    if (hdc != IntPtr.Zero)
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Active window capture failed: " + ex.Message);
            bmp?.Dispose();
            try
            {
                // Because PrintWindow path failed — last resort is region copy of the window rect.
                bmp = GrabRegion(rect.Left, rect.Top, width, height);
            }
            catch
            {
                return null;
            }
        }

        var title = GetWindowTitle(hwnd);
        var program = GetProcessName(hwnd);
        var monitorIndex = FindMonitorIndex(rect.Left + width / 2, rect.Top + height / 2);

        return new ScreenCaptureResult
        {
            Image = bmp!,
            CaptureType = "Window",
            MonitorIndex = monitorIndex >= 0 ? monitorIndex : (int?)null,
            MonitorName = monitorIndex >= 0 ? GetMonitorName(monitorIndex) : null,
            WindowTitle = title,
            ProgramName = program,
            NativeWindowId = hwnd.ToInt64(),
            Description = string.IsNullOrEmpty(title) ? program : title
        };
    }

    /// <summary>
    /// Copies a screen rectangle into a new 32bpp ARGB bitmap.
    /// </summary>
    /// <param name="x">Left in virtual desktop coordinates.</param>
    /// <param name="y">Top in virtual desktop coordinates.</param>
    /// <param name="width">Width in pixels (clamped to at least 1).</param>
    /// <param name="height">Height in pixels (clamped to at least 1).</param>
    /// <returns>Caller-owned bitmap of the region.</returns>
    private static Bitmap GrabRegion(int x, int y, int width, int height)
    {
        if (width < 1)
        {
            width = 1;
        }

        if (height < 1)
        {
            height = 1;
        }

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        return bmp;
    }

    /// <summary>
    /// Finds which monitor contains the point <paramref name="x"/>,<paramref name="y"/>.
    /// </summary>
    /// <param name="x">Virtual desktop X.</param>
    /// <param name="y">Virtual desktop Y.</param>
    /// <returns>Zero-based index, or -1 when outside all monitors.</returns>
    private static int FindMonitorIndex(int x, int y)
    {
        var screens = Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            if (screens[i].Bounds.Contains(x, y))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Reads the window caption text via Win32.</summary>
    /// <param name="hwnd">Window handle.</param>
    /// <returns>Title string, or empty when none.</returns>
    private static string GetWindowTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len <= 0)
        {
            return "";
        }

        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Resolves the process name owning <paramref name="hwnd"/>.</summary>
    /// <param name="hwnd">Window handle.</param>
    /// <returns>Process name without path, or empty on failure.</returns>
    private static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                return "";
            }

            using (var p = Process.GetProcessById((int)pid))
            {
                return p.ProcessName ?? "";
            }
        }
        catch
        {
            return "";
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // nothing to free
    }

    #region Native

    /// <summary>Win32 RECT for window bounds.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        /// <summary>Left edge.</summary>
        public int Left;

        /// <summary>Top edge.</summary>
        public int Top;

        /// <summary>Right edge.</summary>
        public int Right;

        /// <summary>Bottom edge.</summary>
        public int Bottom;
    }

    /// <summary>Returns the foreground window handle.</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>Gets the outer window rectangle in screen coordinates.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>Copies the window title into a buffer.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>Returns the length of the window title (excluding null terminator).</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    /// <summary>Gets the process id that owns the window.</summary>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>Reads the window class name.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>Renders a window into a device context (used for DWM/layered windows).</summary>
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    #endregion
}
