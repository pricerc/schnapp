namespace Schnapp.Client.Linux.Capture;

using System.Threading;
using System.Threading.Tasks;

using Schnapp.Client.Linux.Services;

/// <summary>
/// Screensaver detection via the X11 Screen Saver extension (<c>libXss</c>), in-process.
/// </summary>
/// <remarks>
/// Uses shared <see cref="X11Native"/>: active when <c>state == ScreenSaverOn</c>.
/// Returns false if the extension is missing or the display cannot be opened.
/// </remarks>
public sealed class LinuxScreensaverDetector : IScreensaverDetector
{
    /// <inheritdoc />
    public Task<bool> IsActiveAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(IsActive());
    }

    /// <summary>Synchronous check (X11 calls are not async).</summary>
    public static bool IsActive()
    {
        if (!X11Native.IsAvailable)
        {
            AppLog.Debug("SS: X11 not available for screensaver check");
            return false;
        }

        lock (X11Native.Sync)
        {
            var active = X11Native.IsScreensaverActiveUnlocked(X11Native.Display);
            AppLog.Debug($"SS: IsActive -> {active}");
            return active;
        }
    }
}
