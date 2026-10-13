namespace Schnapp.Utils;

using System.Text.Json;

/// <summary>Walks spool folders and keeps items whose <c>monitorName</c> matches.</summary>
internal static class SpoolCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static List<SpoolMeta> Load(ScanOptions options)
    {
        if (!Directory.Exists(options.SpoolDirectory))
        {
            throw new DirectoryNotFoundException("Spool directory not found: " + options.SpoolDirectory);
        }

        List<SpoolMeta> items = [];
        foreach (string dir in Directory.EnumerateDirectories(options.SpoolDirectory))
        {
            string metaPath = Path.Combine(dir, "meta.json");
            string imagePath = Path.Combine(dir, "image.bin");
            if (!File.Exists(metaPath) || !File.Exists(imagePath))
            {
                continue;
            }

            SpoolMetaFile? raw;
            try
            {
                string json = File.ReadAllText(metaPath);
                raw = JsonSerializer.Deserialize<SpoolMetaFile>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Skip " + dir + ": " + ex.Message);
                continue;
            }

            if (raw is null)
            {
                continue;
            }

            string monitor = raw.MonitorName ?? "";
            if (!options.AllMonitors
                && !string.Equals(monitor, options.MonitorName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateTimeOffset? ts = raw.Timestamp ?? raw.EnqueuedUtc;
            if (ts is null)
            {
                continue;
            }

            string tzId = string.IsNullOrWhiteSpace(raw.ClientTimeZoneId)
                ? "UTC"
                : raw.ClientTimeZoneId.Trim();
            TimeZoneInfo tz = ResolveTimeZone(tzId);
            DateTimeOffset utc = ts.Value.ToUniversalTime();
            if (options.SinceUtc is DateTimeOffset since && utc < since)
            {
                continue;
            }

            if (options.UntilUtc is DateTimeOffset until && utc >= until)
            {
                continue;
            }

            DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, tz);

            items.Add(new SpoolMeta
            {
                Id = raw.Id ?? Path.GetFileName(dir),
                Directory = dir,
                ImagePath = imagePath,
                TimestampUtc = utc,
                TimestampLocal = local,
                TimeZoneId = tz.Id,
                Width = raw.Width ?? 0,
                Height = raw.Height ?? 0,
                ByteSize = raw.ByteSize,
                WebpKind = SniffImageKind(imagePath)
            });
        }

        items.Sort(static (a, b) =>
        {
            int c = a.TimestampUtc.CompareTo(b.TimestampUtc);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });

        if (options.Limit is int limit && limit > 0 && items.Count > limit)
        {
            items.RemoveRange(limit, items.Count - limit);
        }

        return items;
    }

    /// <summary>Reads JPEG SOI or the first WebP chunk tag (VP8L / VP8 / VP8X).</summary>
    internal static string SniffImageKind(string imagePath)
    {
        try
        {
            using FileStream fs = File.OpenRead(imagePath);
            byte[] buf = new byte[16];
            int n = fs.Read(buf, 0, buf.Length);
            if (n < 3)
            {
                return "unknown";
            }

            if (buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF)
            {
                return "jpeg";
            }

            if (n < 16)
            {
                return "unknown";
            }

            if (buf[0] != (byte)'R' || buf[1] != (byte)'I' || buf[2] != (byte)'F' || buf[3] != (byte)'F'
                || buf[8] != (byte)'W' || buf[9] != (byte)'E' || buf[10] != (byte)'B' || buf[11] != (byte)'P')
            {
                return "other";
            }

            string tag = System.Text.Encoding.ASCII.GetString(buf, 12, 4);
            return tag switch
            {
                "VP8L" => "webp-lossless",
                "VP8 " => "webp-lossy",
                "VP8X" => "webp-extended",
                _ => tag.Trim()
            };
        }
        catch
        {
            return "unknown";
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private sealed class SpoolMetaFile
    {
        public string? Id { get; set; }
        public DateTimeOffset? EnqueuedUtc { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public string? ClientTimeZoneId { get; set; }
        public string? MonitorName { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public long ByteSize { get; set; }
    }
}

internal sealed class SpoolMeta
{
    public required string Id { get; init; }
    public required string Directory { get; init; }
    public required string ImagePath { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required DateTimeOffset TimestampLocal { get; init; }
    public required string TimeZoneId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long ByteSize { get; init; }
    public string WebpKind { get; init; } = "";
}
