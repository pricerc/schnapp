namespace Schnapp.Client.Linux.Capture;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Reports how long the user has been idle (no keyboard/mouse input)
/// so the capture timer can skip while unattended.
/// </summary>
public interface IUserIdleDetector
{
    /// <summary>
    /// Returns time since last user input, or <c>null</c> when idle time is unknown
    /// (API failure / unavailable). Callers should fail open (capture) when null.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<TimeSpan?> GetIdleTimeAsync(CancellationToken ct = default);
}
