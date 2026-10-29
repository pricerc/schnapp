namespace Schnapp.Utils;

using System.Text.Json;

using Medo;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Schnapp.Contracts;
using Schnapp.Data;
using Schnapp.Data.Repositories;
using Schnapp.Imaging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// Fingerprints catalog screenshots (storing missing fingerprints back to the DB) and lists
/// blank / idle duplicates per machine + monitor series, using the same rules as the web "Mark duplicates".
/// </summary>
internal static class DupesCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<int> Run(ScanOptions options, CancellationToken ct)
    {
        Console.WriteLine("schnapp-utils dupes");
        Console.WriteLine("Blobs:    " + options.BlobRoot);
        Console.WriteLine("Provider: " + options.Provider);
        Console.WriteLine("Catalog:  " + ServerPaths.RedactConnection(options.ConnectionString));
        Console.WriteLine(
            $"Monitor:  {(options.AllMonitors ? "(all)" : options.MonitorName)}  "
            + $"max dirty cells {options.MaxDirtyCells}  "
            + (options.SaveFingerprints ? "saving missing fingerprints" : "NOT saving fingerprints (--no-save)"));
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Warning);
            b.AddSimpleConsole(o => o.SingleLine = true);
        });
        services.AddSchnappData(options.Provider, options.ConnectionString!, options.BlobRoot);
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var shots = scope.ServiceProvider.GetRequiredService<ScreenshotRepository>();

        var since = options.SinceUtc ?? DateTimeOffset.UnixEpoch;
        var until = options.UntilUtc ?? new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var rows = await shots.ListForDuplicateScanAsync(
            since, until, options.AllMonitors ? null : options.MonitorName, ct);
        if (options.Limit is int limit && rows.Count > limit)
            rows = rows.Take(limit).ToList();
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("No screenshot rows in range.");
            return 2;
        }

        Console.WriteLine($"Catalogued {rows.Count} screenshots.");

        var fps = new FrameFingerprint?[rows.Count];
        var missing = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            fps[i] = FromStored(rows[i]);
            if (fps[i] is null)
                missing.Add(i);
        }

        Console.WriteLine($"Fingerprints stored: {rows.Count - missing.Count}; to compute: {missing.Count}.");

        var computed = 0;
        var failed = 0;
        var saved = 0;
        var progress = new ProgressReporter("Fingerprinted", missing.Count, options.Progress);
        await Parallel.ForEachAsync(missing, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            CancellationToken = ct
        }, async (i, token) =>
        {
            var row = rows[i];
            FrameFingerprint? fp = null;
            try
            {
                fp = Compute(ServerPaths.BlobFile(options.BlobRoot, row.StorageKey));
            }
            catch (Exception ex)
            {
                lock (Console.Error)
                    Console.Error.WriteLine($"Fingerprint failed {Uuid7Text.Format(row.Id)}: {ex.Message}");
            }

            if (fp is null)
            {
                Interlocked.Increment(ref failed);
            }
            else
            {
                fps[i] = fp;
                Interlocked.Increment(ref computed);
                if (options.SaveFingerprints)
                {
                    await shots.UpdateFingerprintAsync(row.Id, row.UserId, ToJson(fp), token);
                    Interlocked.Increment(ref saved);
                }
            }

            progress.Tick(failed: fp is null);
        });

        Console.WriteLine(
            $"Computed {computed}, failed {failed}, saved to DB {saved}"
            + (options.SaveFingerprints ? "." : " (--no-save)."));
        Console.WriteLine();

        var seriesRows = Enumerable.Range(0, rows.Count)
            .GroupBy(i => SeriesKey(rows[i]))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var totalDup = 0;
        var totalBlack = 0;
        foreach (var group in seriesRows)
        {
            var idx = group.ToList();
            var items = idx
                .Select(i => new DuplicateScan.Item(group.Key, rows[i].TimestampUtc, fps[i], i))
                .ToList();
            var verdicts = DuplicateScan.Analyze(items, options.MaxDirtyCells)
                .OrderBy(v => rows[v.Index].TimestampUtc)
                .ToList();
            var black = verdicts.Count(v => v.IsBlack);
            var idle = verdicts.Count - black;
            var noFp = idx.Count(i => fps[i] is null);
            totalDup += idle;
            totalBlack += black;

            Console.WriteLine(
                $"== {group.Key}  {idx.Count} frames, {Local(rows[idx[0]])} → {Local(rows[idx[^1]])}"
                + $"  |  idle dup {idle}, black {black}"
                + (noFp > 0 ? $", no fingerprint {noFp}" : ""));

            int? lastAnchor = null;
            foreach (var v in verdicts)
            {
                var row = rows[v.Index];
                if (v.IsBlack)
                {
                    Console.WriteLine($"   black  {Uuid7Text.Format(row.Id)}  {Local(row)}");
                    continue;
                }

                if (lastAnchor != v.AnchorIndex)
                {
                    lastAnchor = v.AnchorIndex;
                    var anchor = rows[v.AnchorIndex];
                    Console.WriteLine($"   keep   {Uuid7Text.Format(anchor.Id)}  {Local(anchor)}");
                }

                Console.WriteLine(
                    $"     dup  {Uuid7Text.Format(row.Id)}  {Local(row)}  dirty {v.DirtyCells}/{v.TotalCells}");
            }

            Console.WriteLine();
        }

        Console.WriteLine($"Total: {totalDup} idle duplicates, {totalBlack} black frames of {rows.Count}.");
        Console.WriteLine(
            "Nothing is deleted. Review in the web gallery (Mark duplicates, then Delete selected).");
        return 0;
    }

    private static string SeriesKey(ScreenshotDuplicateRow r)
    {
        var machine = string.IsNullOrWhiteSpace(r.MachineName) ? Uuid7Text.Format(r.MachineId) : r.MachineName;
        var monitor = r.MonitorName ?? r.MonitorId ?? "(unknown)";
        return machine + " / " + monitor;
    }

    private static string Local(ScreenshotDuplicateRow r)
    {
        var utc = r.TimestampUtc.ToUniversalTime();
        try
        {
            var tz = string.IsNullOrWhiteSpace(r.ClientTimeZoneId)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(r.ClientTimeZoneId.Trim());
            return TimeZoneInfo.ConvertTime(utc, tz).ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc.ToString("yyyy-MM-dd HH:mm:ss") + "Z";
        }
    }

    private static FrameFingerprint? FromStored(ScreenshotDuplicateRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Fingerprint))
            return null;

        ScreenshotFingerprint? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ScreenshotFingerprint>(row.Fingerprint, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto?.CellMeans is null || dto.GridCols < 1 || dto.GridRows < 1
            || dto.CellMeans.Length != dto.GridCols * dto.GridRows)
            return null;

        return FrameFingerprint.FromStored(
            row.Width ?? 0, row.Height ?? 0, dto.GridCols, dto.GridRows, dto.CellMeans,
            dto.MeanLuma, dto.MaxLuma, ParseHash(dto.AHash), ParseHash(dto.DHash), ParseHash(dto.PHash));
    }

    private static FrameFingerprint Compute(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        int w = image.Width;
        int h = image.Height;
        var buf = new byte[checked(w * h * 4)];
        image.CopyPixelDataTo(buf);
        return FrameFingerprint.FromPixels(w, h, buf, w * 4, PixelLayout.Rgba32);
    }

    // Must match the API's stored shape (camelCase ScreenshotFingerprint).
    private static string ToJson(FrameFingerprint fp) => JsonSerializer.Serialize(new ScreenshotFingerprint
    {
        Version = fp.Version,
        GridCols = fp.GridCols,
        GridRows = fp.GridRows,
        CellMeans = fp.CellMeans,
        MeanLuma = fp.MeanLuma,
        MaxLuma = fp.MaxLuma,
        AHash = fp.AHash.ToString("x16"),
        DHash = fp.DHash.ToString("x16"),
        PHash = fp.PHash.ToString("x16")
    }, Json);

    private static ulong ParseHash(string? hex) =>
        !string.IsNullOrWhiteSpace(hex)
        && ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
}
