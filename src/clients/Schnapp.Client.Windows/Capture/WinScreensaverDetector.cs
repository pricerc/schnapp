namespace Schnapp.Client.Windows.Capture;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Detects screensaver running and workstation lock via Win32.
/// </summary>
/// <remarks>
/// Called from the capture timer path in <c>CaptureService</c> when
/// <c>SkipWhenScreensaverActive</c> is enabled.
/// </remarks>
public sealed class WinScreensaverDetector : IScreensaverDetector
{
    /// <summary>SPI action code for querying whether the screensaver is currently running.</summary>
    private const int SPI_GETSCREENSAVERRUNNING = 0x0072;

    /// <inheritdoc />
    /// <param name="ct">Cancellation token checked before detection work.</param>
    /// <returns>
    /// <c>true</c> when screensaver is running or the workstation is locked;
    /// <c>false</c> on idle desktop or when detection APIs fail (fail-open for capture).
    /// </returns>
    public Task<bool> IsActiveAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (IsScreensaverRunning())
            {
                return Task.FromResult(true);
            }

            if (IsWorkstationLocked())
            {
                return Task.FromResult(true);
            }
        }
        catch
        {
            // Because ignore detection failures — better to capture than to skip forever.
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Queries <c>SystemParametersInfo(SPI_GETSCREENSAVERRUNNING)</c>.
    /// </summary>
    /// <returns><c>true</c> when the OS reports the screensaver is running.</returns>
    private static bool IsScreensaverRunning()
    {
        if (!SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, out var running, 0))
        {
            return false;
        }

        return running;
    }

    /// <summary>
    /// Heuristic lock detection: <c>OpenInputDesktop</c> fails on the secure desktop.
    /// </summary>
    /// <returns><c>true</c> when the input desktop cannot be opened (typically locked).</returns>
    private static bool IsWorkstationLocked()
    {
        // Because OpenInputDesktop fails when the secure desktop (lock screen) is active.
        var hDesk = OpenInputDesktop(0, false, 0x0100 /* DESKTOP_SWITCHDESKTOP */);
        if (hDesk == IntPtr.Zero)
        {
            return true;
        }

        CloseDesktop(hDesk);
        return false;
    }

    /// <summary>Win32 SystemParametersInfo (bool out parameter form).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, out bool pvParam, int fWinIni);

    /// <summary>Opens the desktop that receives user input.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    /// <summary>Closes a desktop handle from <see cref="OpenInputDesktop"/>.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);
}
