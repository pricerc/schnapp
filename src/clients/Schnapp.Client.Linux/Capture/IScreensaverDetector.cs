namespace Schnapp.Client.Linux.Capture;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Detects whether a screensaver/lock screen is active (skip idle captures).
/// </summary>
public interface IScreensaverDetector
{
    /// <summary>
    /// True when a screensaver appears active. False on unknown/unavailable tools.
    /// </summary>
    Task<bool> IsActiveAsync(CancellationToken ct = default);
}
