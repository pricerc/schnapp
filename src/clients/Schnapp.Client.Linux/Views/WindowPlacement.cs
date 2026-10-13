namespace Schnapp.Client.Linux.Views;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Linux.Capture;

/// <summary>
/// Restores a window to a saved pixel position or places it on the tray/cursor screen.
/// </summary>
/// <remarks>
/// Used by dialogs opened from the system tray so they stay on the expected monitor.
/// Saved bounds live in <see cref="ClientSettings"/>.
/// </remarks>
public static class WindowPlacement
{
    /// <summary>
    /// Applies saved bounds if they still intersect a screen; otherwise positions near the tray corner
    /// of the screen that contains the pointer (fallback: primary).
    /// </summary>
    /// <param name="window">Window to position (should use <see cref="WindowStartupLocation.Manual"/>).</param>
    /// <param name="saved">Previously saved bounds, or null for first open.</param>
    public static void RestoreOrPlaceNearTray(Window window, SavedWindowBounds? saved)
    {
        var screens = window.Screens?.All;
        if (screens == null || screens.Count == 0)
        {
            return;
        }


        if (saved is { } b && TryRestore(window, b, screens))
        {
            return;
        }


        PlaceNearTray(window, screens);
    }

    /// <summary>
    /// Centers <paramref name="window"/> on the screen under the pointer (fallback: primary).
    /// Preferred for small prompts that should be movable and not pinned to the tray corner.
    /// </summary>
    public static void PlaceCenteredOnPointerScreen(Window window)
    {
        var screens = window.Screens?.All;
        if (screens == null || screens.Count == 0)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;

        Screen? screen = null;
        if (TryGetPointerPosition(out var pointer))
        {
            screen = window.Screens?.ScreenFromPoint(pointer);
        }

        screen ??= screens.FirstOrDefault(s => s.IsPrimary)
                   ?? screens.OrderByDescending(s => s.Bounds.Right).First();

        var wa = screen.WorkingArea;
        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        double w = LogicalWidth(window, fallback: 360);
        double h = LogicalHeight(window, fallback: 160);
        int pw = (int)(w * scale);
        int ph = (int)(h * scale);
        int x = wa.X + Math.Max(0, (wa.Width - pw) / 2);
        int y = wa.Y + Math.Max(0, (wa.Height - ph) / 2);
        window.Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// Captures the current window position and size for persistence.
    /// </summary>
    /// <param name="window">Open window.</param>
    /// <returns>Pixel position and logical size.</returns>
    public static SavedWindowBounds Capture(Window window)
    {
        return new SavedWindowBounds
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = window.Width > 0 && !double.IsNaN(window.Width) ? window.Width : window.Bounds.Width,
            Height = window.Height > 0 && !double.IsNaN(window.Height) ? window.Height : window.Bounds.Height
        };
    }

    private static bool TryRestore(Window window, SavedWindowBounds saved, IReadOnlyList<Screen> screens)
    {
        if (saved.Width < 100 || saved.Height < 80)
        {
            return false;
        }


        var probe = new PixelPoint(saved.X + 20, saved.Y + 20);
        if (!screens.Any(s => s.WorkingArea.Contains(probe)))
        {

            return false;
        }


        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = saved.Width;
        window.Height = saved.Height;
        window.Position = new PixelPoint(saved.X, saved.Y);
        return true;
    }

    private static void PlaceNearTray(Window window, IReadOnlyList<Screen> screens)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        Screen? screen = null;
        if (TryGetPointerPosition(out var pointer))
        {
            screen = window.Screens?.ScreenFromPoint(pointer);
        }


        screen ??= screens.FirstOrDefault(s => s.IsPrimary)
                   ?? screens.OrderByDescending(s => s.Bounds.Right).First();

        var wa = screen.WorkingArea;
        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        double w = LogicalWidth(window, fallback: 720);
        double h = LogicalHeight(window, fallback: 520);
        int pw = (int)(w * scale);
        int ph = (int)(h * scale);
        int x = wa.Right - pw - (int)(16 * scale);
        int y = wa.Bottom - ph - (int)(48 * scale);
        x = Math.Max(wa.X, Math.Min(x, wa.Right - pw));
        y = Math.Max(wa.Y, Math.Min(y, wa.Bottom - ph));
        window.Position = new PixelPoint(x, y);
    }

    private static double LogicalWidth(Window window, double fallback)
    {
        if (window.Width > 0 && !double.IsNaN(window.Width))
        {
            return window.Width;
        }

        if (window.Bounds.Width > 0 && !double.IsNaN(window.Bounds.Width))
        {
            return window.Bounds.Width;
        }

        return fallback;
    }

    private static double LogicalHeight(Window window, double fallback)
    {
        if (window.Height > 0 && !double.IsNaN(window.Height))
        {
            return window.Height;
        }

        if (window.Bounds.Height > 0 && !double.IsNaN(window.Bounds.Height))
        {
            return window.Bounds.Height;
        }

        return fallback;
    }

    /// <summary>
    /// Reads global pointer position (X11) so tray dialogs/menus open on the monitor hosting the tray.
    /// </summary>
    public static bool TryGetPointerPosition(out PixelPoint point)
    {
        point = default;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {

            return false;
        }


        try
        {
            if (!X11Native.IsAvailable)
            {

                return false;
            }


            lock (X11Native.Sync)
            {
                var display = X11Native.Display;
                var root = X11Native.DefaultRootWindow(display);
                if (X11Native.XQueryPointer(display, root,
                        out _, out _,
                        out var rootX, out var rootY,
                        out _, out _,
                        out _) == 0)
                {

                    return false;
                }


                point = new PixelPoint(rootX, rootY);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
