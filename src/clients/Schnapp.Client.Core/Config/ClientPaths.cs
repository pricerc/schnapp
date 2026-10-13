namespace Schnapp.Client.Core.Config;

using System;
using System.IO;
using System.Linq;

/// <summary>
/// Resolves per-user Schnapp directories (shared by CLI and tray clients).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Config (settings, token): Linux <c>~/.config/schnapp</c>, Windows <c>%AppData%\Schnapp</c></description></item>
/// <item><description>Data (spool, mutations): Linux <c>~/.local/share/schnapp</c>, Windows <c>%LocalAppData%\Schnapp</c></description></item>
/// </list>
/// Override with <c>XDG_CONFIG_HOME</c> / <c>XDG_DATA_HOME</c>, or settings paths / env vars.
/// </remarks>
public static class ClientPaths
{
    /// <summary>
    /// Returns the config directory path, creating it if missing.
    /// Settings and tokens only — not offline queues.
    /// </summary>
    public static string GetConfigDirectory()
    {
        string dir;
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            dir = Path.Combine(appData, "Schnapp");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            dir = string.IsNullOrEmpty(xdg)
                ? Path.Combine(home, ".config", "schnapp")
                : Path.Combine(xdg, "schnapp");
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Returns the data directory path, creating it if missing.
    /// Offline spool and mutation queues live here by default.
    /// </summary>
    public static string GetDataDirectory()
    {
        string dir;
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dir = Path.Combine(local, "Schnapp");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            dir = string.IsNullOrEmpty(xdg)
                ? Path.Combine(home, ".local", "share", "schnapp")
                : Path.Combine(xdg, "schnapp");
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Default screenshot spool root (<c>{data}/spool</c>), unless overridden.
    /// Order: constructor arg → <c>SCHNAPP_SPOOL_DIR</c> → settings → default data path.
    /// Also migrates legacy <c>{config}/spool</c> when the new location is empty.
    /// </summary>
    public static string GetSpoolDirectory(string? settingsOverride = null)
    {
        var configured = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SCHNAPP_SPOOL_DIR"),
            settingsOverride);
        if (!string.IsNullOrEmpty(configured))
        {
            Directory.CreateDirectory(configured);
            return Path.GetFullPath(configured);
        }

        var dest = Path.Combine(GetDataDirectory(), "spool");
        MaybeMigrateDirectory(Path.Combine(GetConfigDirectory(), "spool"), dest);
        Directory.CreateDirectory(dest);
        return dest;
    }

    /// <summary>
    /// Default mutation queue root (<c>{data}/mutations</c>), unless overridden.
    /// Order: constructor arg → <c>SCHNAPP_MUTATIONS_DIR</c> → settings → default data path.
    /// Also migrates legacy <c>{config}/mutations</c> when the new location is empty.
    /// </summary>
    public static string GetMutationsDirectory(string? settingsOverride = null)
    {
        var configured = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SCHNAPP_MUTATIONS_DIR"),
            settingsOverride);
        if (!string.IsNullOrEmpty(configured))
        {
            Directory.CreateDirectory(configured);
            return Path.GetFullPath(configured);
        }

        var dest = Path.Combine(GetDataDirectory(), "mutations");
        MaybeMigrateDirectory(Path.Combine(GetConfigDirectory(), "mutations"), dest);
        Directory.CreateDirectory(dest);
        return dest;
    }

    /// <summary>Path to <c>settings.json</c>.</summary>
    public static string SettingsFile => Path.Combine(GetConfigDirectory(), "settings.json");

    /// <summary>Path to the opaque API token file.</summary>
    public static string TokenFile => Path.Combine(GetConfigDirectory(), "token");

    /// <summary>
    /// Path to the unified client-side SQLite database (<c>{data}/schnapp.db</c>) that holds the
    /// screenshot spool index, reference-data cache, and offline mutation queue
    /// (<c>mutation</c>). (Previously spool/reference/mutations were separate stores.)
    /// </summary>
    public static string GetDatabasePath(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(GetDataDirectory(), "schnapp.db");
    }

    /// <summary>
    /// Default logs directory (<c>{data}/logs</c>).
    /// Dated files: <c>schnapp-YYYY-MM-DD.log</c>.
    /// </summary>
    public static string GetLogsDirectory()
    {
        var dir = Path.Combine(GetDataDirectory(), "logs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (v is null)
            {
                continue;
            }

            var t = v.Trim();
            if (t.Length > 0)
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>
    /// One-time move of legacy queue data from config dir into data dir when dest is empty.
    /// </summary>
    private static void MaybeMigrateDirectory(string source, string dest)
    {
        try
        {
            if (!Directory.Exists(source))
            {
                return;
            }

            if (string.Equals(
                    Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.Ordinal))
            {
                return;
            }

            var hasSource = Directory.EnumerateFileSystemEntries(source).Any();
            if (!hasSource)
            {
                return;
            }

            if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (Directory.Exists(dest))
            {
                Directory.Delete(dest);
            }

            Directory.Move(source, dest);
        }
        catch
        {
            // leave data in place if migrate fails
        }
    }
}
