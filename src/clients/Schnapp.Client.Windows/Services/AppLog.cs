namespace Schnapp.Client.Windows.Services;

using System;
using System.IO;

using Schnapp.Client.Core.Config;

/// <summary>
/// Minimal console + dated file logger for debugging the Windows tray client.
/// </summary>
/// <remarks>
/// Console enabled by command-line switches: --log-console, -v, --verbose.
/// File logging level is selectable in settings (Off/Error/Warn/Info/Debug).
/// </remarks>
public static class AppLog
{
    /// <summary>
    /// When true, every write also goes to <see cref="Console.WriteLine"/>
    /// (regardless of <see cref="FileLevel"/>).
    /// </summary>
    public static bool ConsoleEnabled { get; set; }

    /// <summary>
    /// Minimum severity written to the daily log file under the client logs directory.
    /// <see cref="ClientSettings.ClientLogLevel.Off"/> disables file output.
    /// </summary>
    public static ClientSettings.ClientLogLevel FileLevel { get; set; } = ClientSettings.ClientLogLevel.Info;

    /// <summary>Serializes path rollover and appends so concurrent ticks do not interleave badly.</summary>
    private static readonly object Sync = new();

    /// <summary>UTC date stamp of the open log file (yyyy-MM-dd), used for midnight rollover.</summary>
    private static string? _currentDate;

    /// <summary>Full path of the current day's log file, or null before first write.</summary>
    private static string? _logPath;

    /// <summary>
    /// Parse simple switches from raw args before WinForms starts.
    /// </summary>
    /// <param name="args">Process command-line arguments.</param>
    public static void InitFromArgs(string[] args)
    {
        foreach (var a in args)
        {
            var s = a.Trim();
            if (s.Equals("--log-console", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("--verbose", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("--log-console=true", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleEnabled = true;
            }
            if (s.Equals("--log-console=false", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleEnabled = false;
            }
        }
    }

    /// <summary>
    /// Apply file logging level from persisted settings.
    /// </summary>
    /// <param name="settings">Loaded client settings (uses <see cref="ClientSettings.FileLogLevel"/>).</param>
    public static void InitFileFromSettings(ClientSettings settings)
    {
        FileLevel = settings.FileLogLevel;
    }

    /// <summary>Writes a debug-level line when file/console filtering allows it.</summary>
    /// <param name="message">Log message body (no severity prefix).</param>
    public static void Debug(string message) => Write(ClientSettings.ClientLogLevel.Debug, "DEBUG", message);

    /// <summary>Writes an informational line when file/console filtering allows it.</summary>
    /// <param name="message">Log message body (no severity prefix).</param>
    public static void Info(string message) => Write(ClientSettings.ClientLogLevel.Info, "INFO", message);

    /// <summary>Writes a warning line when file/console filtering allows it.</summary>
    /// <param name="message">Log message body (no severity prefix).</param>
    public static void Warn(string message) => Write(ClientSettings.ClientLogLevel.Warn, "WARN", message);

    /// <summary>Writes an error line when file/console filtering allows it.</summary>
    /// <param name="message">Log message body (no severity prefix).</param>
    public static void Error(string message) => Write(ClientSettings.ClientLogLevel.Error, "ERROR", message);

    /// <summary>Logs an error with exception type, message, and stack (best-effort).</summary>
    /// <param name="message">Context prefix before the exception details.</param>
    /// <param name="ex">Exception to append; when null, behaves like <see cref="Error(string)"/>.</param>
    public static void Error(string message, Exception? ex)
    {
        if (ex == null)
        {
            Error(message);
            return;
        }

        try
        {
            Error(message + ": " + ex.GetType().FullName + ": " + ex.Message
                  + Environment.NewLine + ex.ToString());
        }
        catch
        {
            // Because ToString can throw on broken exception graphs — fall back to Message only.
            Error(message + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Core write path: optional console + optional dated file under the client logs directory.
    /// Never throws to callers.
    /// </summary>
    /// <param name="msgLevel">Severity of this message (compared to <see cref="FileLevel"/>).</param>
    /// <param name="label">Bracket tag in the line (DEBUG/INFO/WARN/ERROR).</param>
    /// <param name="message">Message body.</param>
    private static void Write(ClientSettings.ClientLogLevel msgLevel, string label, string message)
    {
        if (FileLevel == ClientSettings.ClientLogLevel.Off && !ConsoleEnabled)
        {
            return;
        }

        // Because console-only verbose mode still wants DEBUG even when file is Info.
        if (msgLevel > FileLevel && !ConsoleEnabled)
        {
            return;
        }

        var line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + label + "] " + message;

        if (ConsoleEnabled)
        {
            try { Console.WriteLine(line); } catch { /* ignore */ }
        }

        if (FileLevel == ClientSettings.ClientLogLevel.Off || msgLevel > FileLevel)
        {
            return;
        }

        try
        {
            var dir = ClientPaths.GetLogsDirectory();
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string path;

            lock (Sync)
            {
                if (_currentDate != date || _logPath == null)
                {
                    // Because rotate at UTC midnight so each day gets its own file.
                    _currentDate = date;
                    _logPath = Path.Combine(dir, "schnapp-" + date + ".log");
                }
                path = _logPath;
            }

            lock (Sync)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Because never throw from logging — capture must keep running.
        }
    }
}
