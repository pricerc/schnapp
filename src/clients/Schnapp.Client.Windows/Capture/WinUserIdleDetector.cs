namespace Schnapp.Client.Windows.Capture;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// User idle time via Win32 <c>GetLastInputInfo</c>.
/// </summary>
/// <remarks>
/// Called from the capture timer when <c>IdlePauseMinutes</c> is greater than zero.
/// Fail-open: returns null on API errors so capture is not blocked permanently.
/// </remarks>
public sealed class WinUserIdleDetector : IUserIdleDetector
{
    /// <inheritdoc />
    public Task<TimeSpan?> GetIdleTimeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var info = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
            };
            if (!GetLastInputInfo(ref info))
            {
                return Task.FromResult<TimeSpan?>(null);
            }

            // Because TickCount and dwTime are 32-bit ticks that wrap ~49 days;
            // unsigned subtract yields correct idle ms across wrap.
            var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
            return Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(idleMs));
        }
        catch
        {
            return Task.FromResult<TimeSpan?>(null);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
