using System.Text.Json;
using System.Text.RegularExpressions;
using Schnapp.Data;

namespace Schnapp.Utils;

/// <summary>Resolves Schnapp install root, blob root, and catalog connection.</summary>
internal static class ServerPaths
{
    public const string DefaultRoot = "/srv/schnapp";

    public static string ResolveRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
            return Path.GetFullPath(explicitRoot);

        if (Directory.Exists(DefaultRoot))
            return Path.GetFullPath(DefaultRoot);

        if (Directory.Exists("/opt/schnapp"))
            return Path.GetFullPath("/opt/schnapp");

        return Path.GetFullPath(DefaultRoot);
    }

    public static string ResolveBlobRoot(string schnappRoot, string? explicitBlobs, JsonElement? settings)
    {
        if (!string.IsNullOrWhiteSpace(explicitBlobs))
            return Path.GetFullPath(explicitBlobs);

        if (settings is JsonElement el
            && el.TryGetProperty("Schnapp", out var schnapp)
            && schnapp.TryGetProperty("BlobRoot", out var blob)
            && blob.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(blob.GetString()))
        {
            var configured = blob.GetString()!;
            // Production config often says /opt/schnapp; this machine mounts the same tree at --root.
            if (configured.StartsWith("/opt/schnapp/", StringComparison.Ordinal)
                && schnappRoot != "/opt/schnapp")
            {
                var mapped = schnappRoot.TrimEnd('/') + configured["/opt/schnapp".Length..];
                if (Directory.Exists(mapped))
                    return Path.GetFullPath(mapped);
            }

            return Path.GetFullPath(configured);
        }

        var underRoot = Path.Combine(schnappRoot, "data", "blobs");
        if (Directory.Exists(underRoot))
            return Path.GetFullPath(underRoot);

        return Path.GetFullPath(underRoot);
    }

    public static string? ResolveConnectionString(string schnappRoot, string? explicitCs, JsonElement? settings)
    {
        if (!string.IsNullOrWhiteSpace(explicitCs))
            return explicitCs;

        var env = Environment.GetEnvironmentVariable("SCHNAPP_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Main");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        if (settings is JsonElement el
            && el.TryGetProperty("ConnectionStrings", out var css)
            && css.TryGetProperty("Main", out var main)
            && main.ValueKind == JsonValueKind.String)
        {
            return main.GetString();
        }

        return null;
    }

    public static DatabaseProvider ResolveProvider(string? explicitProvider, JsonElement? settings)
    {
        if (!string.IsNullOrWhiteSpace(explicitProvider))
            return DatabaseProviderParser.Parse(explicitProvider);

        if (settings is JsonElement el
            && el.TryGetProperty("Schnapp", out var schnapp)
            && schnapp.TryGetProperty("DatabaseProvider", out var p)
            && p.ValueKind == JsonValueKind.String)
        {
            return DatabaseProviderParser.Parse(p.GetString());
        }

        return DatabaseProvider.PostgreSQL;
    }

    public static JsonElement? TryLoadSettings(string schnappRoot)
    {
        string[] candidates =
        [
            Path.Combine(schnappRoot, "config", "api.appsettings.Production.json"),
            Path.Combine(schnappRoot, "api", "appsettings.Production.json"),
            Path.Combine(schnappRoot, "api", "appsettings.json")
        ];
        foreach (var path in candidates)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(info.FullName));
                return doc.RootElement.Clone();
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Dangling production symlink is normal on a files-only mount.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Skip settings " + path + ": " + ex.Message);
            }
        }

        return null;
    }

    public static string BlobFile(string blobRoot, string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage key: " + storageKey);
        var root = Path.GetFullPath(blobRoot);
        if (!root.EndsWith(Path.DirectorySeparatorChar) && !root.EndsWith(Path.AltDirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, storageKey));
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new ArgumentException("Storage key escapes blob root: " + storageKey);
        return full;
    }

    /// <summary>Connection string with password values replaced, for console banners.</summary>
    public static string RedactConnection(string? cs)
    {
        if (string.IsNullOrWhiteSpace(cs))
            return "(none)";
        return Regex.Replace(cs, "(?i)(Password|Pwd|Pass)\\s*=\\s*[^;]*", "$1=***");
    }
}
