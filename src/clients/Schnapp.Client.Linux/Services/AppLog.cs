namespace Schnapp.Client.Linux.Services;

using Schnapp.Client.Core.Config;

using System;
using System.IO;

/// <summary>
/// Minimal console + dated file logger for debugging.
/// Console enabled by command-line switches: --log-console, -v, --verbose.
/// File logging level is selectable in settings (Off/Error/Warn/Info/Debug).
/// </summary>
public static class AppLog
{
    public static bool ConsoleEnabled { get; set; }
    public static ClientSettings.ClientLogLevel FileLevel { get; set; } = ClientSettings.ClientLogLevel.Info;

    private static readonly Lock _lock = new();
    private static string? _currentDate;
    private static string? _logPath;

    /// <summary>
    /// Parse simple switches from raw args before Avalonia takes them.
    /// </summary>
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
    public static void InitFileFromSettings(ClientSettings settings)
    {
        FileLevel = settings.FileLogLevel;
    }

    public static void Debug(string message) => Write(ClientSettings.ClientLogLevel.Debug, "DEBUG", message);
    public static void Info(string message) => Write(ClientSettings.ClientLogLevel.Info, "INFO", message);
    public static void Warn(string message) => Write(ClientSettings.ClientLogLevel.Warn, "WARN", message);
    public static void Error(string message) => Write(ClientSettings.ClientLogLevel.Error, "ERROR", message);

    private static void Write(ClientSettings.ClientLogLevel msgLevel, string label, string message)
    {
        if (FileLevel == ClientSettings.ClientLogLevel.Off && !ConsoleEnabled)
        {
            return;
        }


        if (msgLevel > FileLevel && !ConsoleEnabled)
        {
            return;
        }


        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{label}] {message}";

        if (ConsoleEnabled)
        {
            try { Console.WriteLine(line); } catch { }
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

            lock (_lock)
            {
                if (_currentDate != date || _logPath == null)
                {
                    _currentDate = date;
                    _logPath = Path.Combine(dir, $"schnapp-{date}.log");
                }
                path = _logPath;
            }

            lock (_lock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // never throw from logging
        }
    }
}
