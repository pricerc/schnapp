namespace Schnapp.Client.Linux.Capture;

using System;
using System.Threading;
using System.Threading.Tasks;

using Schnapp.Client.Linux.Services;

/// <summary>
/// User idle time via the X11 Screen Saver extension (<c>libXss</c>), in-process.
/// </summary>
/// <remarks>
/// Uses shared <see cref="X11Native"/> <c>XScreenSaverInfo.idle</c> (milliseconds).
/// Returns null if X11/Xss is unavailable (e.g. pure Wayland) — fail-open for capture.
/// </remarks>
public sealed class LinuxUserIdleDetector : IUserIdleDetector
{
    /// <inheritdoc />
    public Task<TimeSpan?> GetIdleTimeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GetIdleTime());
    }

    /// <summary>Synchronous check (X11 calls are not async).</summary>
    public static TimeSpan? GetIdleTime()
    {
        if (!X11Native.IsAvailable)
        {
            AppLog.Debug("Idle: X11 not available for idle check");
            return null;
        }

        lock (X11Native.Sync)
        {
            if (!X11Native.TryGetIdleTimeUnlocked(X11Native.Display, out var idle))
            {
                AppLog.Debug("Idle: query failed or Xss unavailable");
                return null;
            }

            AppLog.Debug($"Idle: GetIdleTime -> {idle.TotalSeconds:F1}s");
            return idle;
        }
    }
}
