namespace Schnapp.Client.Core.Config;

using System;
using Schnapp.Contracts;
using System.IO;
using System.Text;
using System.Text.Json;

/// <summary>
/// Loads and saves <see cref="ClientSettings"/> and the API token file.
/// </summary>
public sealed class ClientSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    }.AddUuid7Converters();

    /// <summary>
    /// Loads settings from disk, or defaults if the file is missing.
    /// </summary>
    public ClientSettings Load()
    {
        var path = ClientPaths.SettingsFile;
        if (!File.Exists(path))
        {

            return new ClientSettings();
        }


        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions) ?? new ClientSettings();
        settings.NormalizeCaptureTargets();
        return settings;
    }

    /// <summary>
    /// Writes settings to disk.
    /// </summary>
    /// <param name="settings">Settings to persist.</param>
    public void Save(ClientSettings settings)
    {
        settings.NormalizeCaptureTargets();
        settings.CaptureMode = null;
        var path = ClientPaths.SettingsFile;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Reads the stored bearer token, or null.
    /// </summary>
    public string? LoadToken()
    {
        var path = ClientPaths.TokenFile;
        if (!File.Exists(path))
        {

            return null;
        }


        var t = File.ReadAllText(path).Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>
    /// Saves the bearer token (overwrites). Attempts restrictive permissions on Unix.
    /// </summary>
    /// <param name="token">Plaintext API token.</param>
    public void SaveToken(string token)
    {
        var path = ClientPaths.TokenFile;
        File.WriteAllText(path, token ?? "", Encoding.UTF8);
        TryRestrictUnixFile(path);
    }

    /// <summary>
    /// Deletes the token file (logout).
    /// </summary>
    public void ClearToken()
    {
        var path = ClientPaths.TokenFile;
        if (File.Exists(path))
        {
            File.Delete(path);
        }

    }

    private static void TryRestrictUnixFile(string path)
    {
        try
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix
                || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                // 600
                if (Type.GetType("Mono.Unix.Native.Syscall, Mono.Posix") != null)
                {
                    // skip mono-specific
                }
                // best-effort via chmod process is heavy; File.SetUnixFileMode on net7+
#if NET7_0_OR_GREATER
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#endif
            }
        }
        catch
        {
            // ignore
        }
    }
}
