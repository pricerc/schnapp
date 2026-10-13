namespace Schnapp.Client.Windows.Forms;

using System.Drawing;
using System.Windows.Forms;

using Schnapp.Client.Core.Config;

/// <summary>
/// Restores a window to a saved pixel position or places it near the system tray.
/// </summary>
/// <remarks>
/// Used by dialogs opened from the tray so they stay on the expected monitor.
/// Saved bounds live in <see cref="ClientSettings"/>.
/// </remarks>
public static class WindowPlacement
{
    /// <summary>
    /// Applies saved bounds if they still intersect a screen; otherwise positions near the tray.
    /// </summary>
    /// <param name="form">Form whose position will be set.</param>
    /// <param name="saved">Previously captured bounds, or null to place near the tray.</param>
    public static void RestoreOrPlaceNearTray(Form form, SavedWindowBounds? saved)
    {
        if (saved != null && TryRestore(form, saved))
        {
            return;
        }

        PlaceNearTray(form);
    }

    /// <summary>Captures the current window position and size for persistence.</summary>
    /// <param name="form">Form to capture.</param>
    /// <returns>Pixel bounds suitable for <see cref="ClientSettings"/>.</returns>
    public static SavedWindowBounds Capture(Form form)
    {
        return new SavedWindowBounds
        {
            X = form.Left,
            Y = form.Top,
            Width = form.Width,
            Height = form.Height
        };
    }

    /// <summary>
    /// Tries to restore <paramref name="form"/> to <paramref name="saved"/> if still on-screen.
    /// </summary>
    /// <param name="form">Form to position.</param>
    /// <param name="saved">Saved bounds to apply.</param>
    /// <returns>
    /// <c>true</c> if bounds were applied; <c>false</c> if size was invalid or the probe point
    /// no longer intersects any screen working area.
    /// </returns>
    private static bool TryRestore(Form form, SavedWindowBounds saved)
    {
        // Because tiny saved sizes usually mean a corrupt/default record; fall through to tray place.
        if (saved.Width < 100 || saved.Height < 80)
        {
            return false;
        }

        // Because probing slightly inside the title bar avoids accepting a window fully off-screen
        // when only a thin strip of the old bounds still clips a monitor edge.
        var probe = new Point(saved.X + 20, saved.Y + 20);
        var onScreen = false;
        foreach (var s in Screen.AllScreens)
        {
            if (s.WorkingArea.Contains(probe))
            {
                onScreen = true;
                break;
            }
        }

        if (!onScreen)
        {
            return false;
        }

        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new Rectangle(saved.X, saved.Y, (int)saved.Width, (int)saved.Height);
        return true;
    }

    /// <summary>
    /// Positions <paramref name="form"/> near the bottom-right of the screen under the cursor
    /// (typical tray-notification corner).
    /// </summary>
    /// <param name="form">Form to position with <see cref="FormStartPosition.Manual"/>.</param>
    private static void PlaceNearTray(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        // Because the tray lives on the monitor with the cursor, not always the primary screen.
        var screen = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
        if (screen == null)
        {
            return;
        }

        var wa = screen.WorkingArea;
        // Because the form may not have a final size yet when first shown.
        var w = form.Width > 0 ? form.Width : 440;
        var h = form.Height > 0 ? form.Height : 420;
        // Because 16/48 px margins keep the dialog clear of the taskbar/tray edge.
        var x = wa.Right - w - 16;
        var y = wa.Bottom - h - 48;
        if (x < wa.X)
        {
            x = wa.X;
        }

        if (y < wa.Y)
        {
            y = wa.Y;
        }

        form.Location = new Point(x, y);
    }
}
