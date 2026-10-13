namespace Schnapp.Client.Linux.Services;

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
    /// <summary>Severity used for color coding.</summary>
    public TrayStatusLevel Level { get; init; } = TrayStatusLevel.Info;

    /// <summary>Brief label shown in the tray menu (a few words).</summary>
    public string ShortLabel { get; init; } = "Idle";

    /// <summary>Full explanation shown when the user clicks the status item.</summary>
    public string Detail { get; init; } = "";

    /// <summary>When this status was set (local time for display).</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// Formats a short colored menu line (emoji works on native menus that lack Foreground).
    /// </summary>
    public string MenuHeader => Level switch
    {
        TrayStatusLevel.Ok => "🟢  " + ShortLabel,
        TrayStatusLevel.Warning => "🟠  " + ShortLabel,
        TrayStatusLevel.Error => "🔴  " + ShortLabel,
        _ => "⚪  " + ShortLabel
    };

    /// <summary>Tooltip without relying on color.</summary>
    public string Tooltip => $"Schnapp — {ShortLabel}";
}
