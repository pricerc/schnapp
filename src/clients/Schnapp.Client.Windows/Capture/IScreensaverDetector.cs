namespace Schnapp.Client.Windows.Capture;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Detects whether a screensaver (or locked session) is active so the timer can skip captures.
/// </summary>
public interface IScreensaverDetector
{
    /// <summary>
    /// Returns true when capture should be skipped for inactivity protection.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when screensaver/lock is active; <c>false</c> otherwise.
    /// Implementations should fail open (return false) on detection errors.
    /// </returns>
    Task<bool> IsActiveAsync(CancellationToken ct = default);
}
