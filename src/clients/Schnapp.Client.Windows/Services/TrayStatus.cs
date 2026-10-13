namespace Schnapp.Client.Windows.Services;

using System;

/// <summary>Severity for tray status display.</summary>
public enum TrayStatusLevel
{
    /// <summary>Neutral / idle / informational.</summary>
    Info,

    /// <summary>Healthy / last operation succeeded.</summary>
    Ok,

    /// <summary>Degraded (paused, offline queue, spool backlog).</summary>
    Warning,

    /// <summary>Failure requiring attention.</summary>
    Error
}

/// <summary>
/// Short tray label plus optional detail for the status dialog.
/// </summary>
public sealed class TrayStatus
{
    /// <summary>Severity used for icon selection.</summary>
    public TrayStatusLevel Level { get; set; } = TrayStatusLevel.Info;

    /// <summary>Brief label shown in the tray menu (a few words).</summary>
    public string ShortLabel { get; set; } = "Idle";

    /// <summary>Full explanation shown when the user clicks the status item.</summary>
    public string Detail { get; set; } = "";

    /// <summary>When this status was set (local time for display).</summary>
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Formats a short menu line (emoji prefix mirrors Linux tray).
    /// </summary>
    /// <remarks>
    /// Color is conveyed via emoji so monochrome tray menus still show severity.
    /// </remarks>
    public string MenuHeader
    {
        get
        {
            return Level switch
            {
                TrayStatusLevel.Ok => "🟢  " + ShortLabel,
                TrayStatusLevel.Warning => "🟠  " + ShortLabel,
                TrayStatusLevel.Error => "🔴  " + ShortLabel,
                _ => "⚪  " + ShortLabel,
            };
        }
    }

    /// <summary>
    /// Tooltip text without relying on color (emoji-free <see cref="ShortLabel"/>).
    /// </summary>
    public string Tooltip => ShortLabel;
}
