namespace Schnapp.Utils;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Schnapp.Contracts;
using Schnapp.Data;
using Schnapp.Data.Repositories;

/// <summary>Loads idle-scan frames from server blob storage using the catalog.</summary>
internal static class BlobCatalog
{
    public static List<SpoolMeta> Load(ScanOptions options)
    {
        var connectionString = options.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Blob idle-scan needs a catalog connection (--connection or SCHNAPP_CONNECTION).");
        }

        var blobRoot = options.BlobRoot;
        if (string.IsNullOrWhiteSpace(blobRoot) || !Directory.Exists(blobRoot))
        {
            throw new DirectoryNotFoundException("Blob root not found: " + blobRoot);
        }

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Warning);
            b.AddSimpleConsole(o => o.SingleLine = true);
        });
        services.AddSchnappData(options.Provider, connectionString, blobRoot);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var shots = scope.ServiceProvider.GetRequiredService<ScreenshotRepository>();

        var since = options.SinceUtc ?? DateTimeOffset.UnixEpoch;
        var until = options.UntilUtc ?? new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string? monitor = options.AllMonitors ? null : options.MonitorName;

        var rows = shots.ListForIdleScanAsync(since, until, monitor, options.Limit)
            .GetAwaiter().GetResult();
        if (rows.Count == 0)
        {
            var monitors = shots.ListMonitorCountsAsync(since, until).GetAwaiter().GetResult();
            Console.Error.WriteLine(
                options.AllMonitors
                    ? "No screenshot rows in range."
                    : "No screenshot rows for monitor '" + options.MonitorName + "' in range.");
            if (monitors.Count > 0)
            {
                Console.Error.WriteLine("Monitors in range:");
                foreach (var m in monitors.Take(20))
                {
                    Console.Error.WriteLine($"  {(m.MonitorName ?? "(null)")}  {m.N}");
                }
            }

            return [];
        }

        List<SpoolMeta> items = [];
        int missing = 0;
        foreach (var row in rows)
        {
            string path;
            try
            {
                path = ServerPaths.BlobFile(blobRoot, row.StorageKey);
            }
            catch
            {
                missing++;
                continue;
            }

            if (!File.Exists(path))
            {
                missing++;
                continue;
            }

            var tzId = string.IsNullOrWhiteSpace(row.ClientTimeZoneId) ? "UTC" : row.ClientTimeZoneId.Trim();
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            }
            catch (TimeZoneNotFoundException)
            {
                tz = TimeZoneInfo.Utc;
            }
            catch (InvalidTimeZoneException)
            {
                tz = TimeZoneInfo.Utc;
            }

            var utc = row.TimestampUtc.ToUniversalTime();
            items.Add(new SpoolMeta
            {
                Id = Uuid7Text.Format(row.Id),
                Directory = Path.GetDirectoryName(path) ?? blobRoot,
                ImagePath = path,
                TimestampUtc = utc,
                TimestampLocal = TimeZoneInfo.ConvertTime(utc, tz),
                TimeZoneId = tz.Id,
                Width = row.Width ?? 0,
                Height = row.Height ?? 0,
                ByteSize = row.ByteSize,
                WebpKind = SpoolCatalog.SniffImageKind(path)
            });
        }

        if (missing > 0)
            Console.Error.WriteLine($"Blob files missing on disk: {missing}/{rows.Count}.");

        items.Sort(static (a, b) =>
        {
            int c = a.TimestampUtc.CompareTo(b.TimestampUtc);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        return items;
    }
}
