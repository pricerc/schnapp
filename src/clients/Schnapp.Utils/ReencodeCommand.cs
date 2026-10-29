namespace Schnapp.Utils;

using System.Security.Cryptography;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Schnapp.Contracts;
using Schnapp.Data;
using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Screenshots;

using Medo;

/// <summary>
/// Rewrites stored screenshot originals as JPEG with EXIF + Schnapp XMP, and regenerates all thumb tiers.
/// </summary>
internal static class ReencodeCommand
{
    public static async Task<int> Run(string[] args, CancellationToken ct)
    {
        if (args.Any(static a => a is "-h" or "--help" or "help" or "-?"))
        {
            PrintHelp();
            return 0;
        }

        ReencodeOptions opt;
        try
        {
            opt = Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintHelp();
            return 1;
        }

        var settings = ServerPaths.TryLoadSettings(opt.Root);
        var blobRoot = ServerPaths.ResolveBlobRoot(opt.Root, opt.BlobRoot, settings);
        var cs = ServerPaths.ResolveConnectionString(opt.Root, opt.ConnectionString, settings);
        var provider = ServerPaths.ResolveProvider(opt.Provider, settings);
        if (string.IsNullOrWhiteSpace(cs))
        {
            Console.Error.WriteLine(
                "Catalog connection string required (--connection, SCHNAPP_CONNECTION, or appsettings ConnectionStrings:Main).");
            return 1;
        }

        if (!Directory.Exists(blobRoot))
        {
            Console.Error.WriteLine("Blob root not found: " + blobRoot);
            return 1;
        }

        Console.WriteLine("schnapp-utils reencode");
        Console.WriteLine("Root:     " + opt.Root);
        Console.WriteLine("Blobs:    " + blobRoot);
        Console.WriteLine("Provider: " + provider);
        Console.WriteLine("Catalog:  " + ServerPaths.RedactConnection(cs));
        Console.WriteLine(
            $"Range:    {opt.SinceUtc:yyyy-MM-dd HH:mm}Z → {opt.UntilUtc:yyyy-MM-dd HH:mm}Z  jobs={opt.Jobs}  "
            + (opt.DryRun ? "DRY-RUN" : "write"));
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Warning);
            b.AddSimpleConsole(o => o.SingleLine = true);
        });
        services.AddSchnappData(provider, cs, blobRoot);
        services.AddSingleton<ScreenshotImageProcessor>();
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var shots = scope.ServiceProvider.GetRequiredService<ScreenshotRepository>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<SettingRepository>();
        var images = scope.ServiceProvider.GetRequiredService<ScreenshotImageProcessor>();

        var jpegQuality = opt.JpegQuality
            ?? await settingsRepo.GetIntAsync(
                SettingKeys.ScreenshotJpegQuality,
                SettingKeys.Defaults.ScreenshotJpegQuality,
                ct);
        jpegQuality = Math.Clamp(jpegQuality, 1, 100);
        Console.WriteLine($"JPEG quality {jpegQuality}, thumb tiers {string.Join(", ", ThumbTiers.Sizes)}.");

        Console.Error.WriteLine("Counting catalog rows…");
        var catalogTotal = await shots.CountForRewriteAsync(opt.SinceUtc, opt.UntilUtc, ct);
        var planned = opt.Limit is int lim ? Math.Min(lim, catalogTotal) : catalogTotal;
        Console.WriteLine(
            opt.Limit is int shownLimit
                ? $"Catalog {catalogTotal:N0} screenshots in range; --limit {shownLimit} → {planned:N0} to process."
                : $"Catalog {catalogTotal:N0} screenshots in range.");
        if (planned == 0)
        {
            Console.WriteLine("Nothing to do.");
            return 0;
        }

        var rewritten = 0;
        var skipped = 0;
        var missing = 0;
        var failed = 0;
        var bytesIn = 0L;
        var bytesOut = 0L;
        var done = 0;
        var missingLogged = 0;
        var failedLogged = 0;
        var progress = new ProgressClock();
        DateTimeOffset? cursorTs = null;
        Uuid7? cursorId = null;
        var started = DateTime.UtcNow;
        Console.Error.WriteLine("Rewriting…");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var take = 200;
                if (opt.Limit is int limit)
                {
                    take = Math.Min(take, Math.Max(0, limit - done));
                    if (take == 0)
                        break;
                }

                var batch = await shots.ListForRewriteAsync(
                    opt.SinceUtc, opt.UntilUtc, cursorTs, cursorId, take, ct);
                if (batch.Count == 0)
                    break;

                var last = batch[^1];
                cursorTs = last.TimestampUtc;
                cursorId = last.Id;

                await Parallel.ForEachAsync(batch, new ParallelOptions
                {
                    MaxDegreeOfParallelism = opt.Jobs,
                    CancellationToken = ct
                }, async (row, token) =>
                {
                    var result = await RewriteOneAsync(
                        row, blobRoot, images, shots, jpegQuality, opt, token);
                    if (result.Detail is not null)
                    {
                        var cap = result.Kind == RewriteKind.Missing
                            ? Interlocked.Increment(ref missingLogged)
                            : Interlocked.Increment(ref failedLogged);
                        if (cap <= 8)
                        {
                            lock (Console.Error)
                                Console.Error.WriteLine(result.Detail);
                        }
                        else if (cap == 9)
                        {
                            lock (Console.Error)
                            {
                                Console.Error.WriteLine(
                                    result.Kind == RewriteKind.Missing
                                        ? "Further missing files counted in progress only."
                                        : "Further failures counted in progress only.");
                            }
                        }
                    }
                    switch (result.Kind)
                    {
                        case RewriteKind.Rewritten:
                            Interlocked.Increment(ref rewritten);
                            Interlocked.Add(ref bytesIn, result.BytesIn);
                            Interlocked.Add(ref bytesOut, result.BytesOut);
                            break;
                        case RewriteKind.Skipped:
                            Interlocked.Increment(ref skipped);
                            break;
                        case RewriteKind.Missing:
                            Interlocked.Increment(ref missing);
                            break;
                        default:
                            Interlocked.Increment(ref failed);
                            break;
                    }

                    Interlocked.Exchange(ref progress.LastAtTicks, row.TimestampUtc.UtcTicks);
                    var n = Interlocked.Increment(ref done);
                    MaybeLogProgress(
                        progress, n, planned, started,
                        Volatile.Read(ref rewritten), Volatile.Read(ref skipped),
                        Volatile.Read(ref missing), Volatile.Read(ref failed),
                        Interlocked.Read(ref bytesIn), Interlocked.Read(ref bytesOut),
                        force: false);
                });

                if (opt.Limit is int cap && done >= cap)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
        }

        MaybeLogProgress(
            progress, done, planned, started,
            rewritten, skipped, missing, failed, bytesIn, bytesOut,
            force: true);

        var elapsed = DateTime.UtcNow - started;
        Console.WriteLine();
        Console.WriteLine(
            $"Done in {FormatDuration(elapsed)}  rewritten {rewritten}  skipped {skipped}  "
            + $"missing {missing}  failed {failed}");
        if (rewritten > 0)
        {
            Console.WriteLine(
                $"Bytes in {FormatBytes(bytesIn)} → JPEG {FormatBytes(bytesOut)} "
                + $"({(bytesIn == 0 ? 0 : 100.0 * bytesOut / bytesIn):0.0}% of source).");
        }

        return failed > 0 ? 2 : 0;
    }

    private static async Task<RewriteResult> RewriteOneAsync(
        ScreenshotRewriteRow row,
        string blobRoot,
        ScreenshotImageProcessor images,
        ScreenshotRepository shots,
        int jpegQuality,
        ReencodeOptions opt,
        CancellationToken ct)
    {
        string srcPath;
        try
        {
            srcPath = ServerPaths.BlobFile(blobRoot, row.StorageKey);
        }
        catch (Exception ex)
        {
            return new RewriteResult(RewriteKind.Failed, 0, 0, Uuid7Text.Format(row.Id) + " bad key: " + ex.Message);
        }

        if (!File.Exists(srcPath))
            return new RewriteResult(RewriteKind.Missing, 0, 0, "Missing " + row.StorageKey);

        byte[] sourceBytes;
        try
        {
            sourceBytes = await File.ReadAllBytesAsync(srcPath, ct);
        }
        catch (Exception ex)
        {
            return new RewriteResult(RewriteKind.Failed, 0, 0, "Read " + row.StorageKey + ": " + ex.Message);
        }

        var alreadyJpeg = row.StorageKey.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            && (row.ContentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
                || row.ContentType.Contains("jpg", StringComparison.OrdinalIgnoreCase));
        if (!opt.Force && alreadyJpeg && ScreenshotFileMetadata.ContainsSchnappNamespace(sourceBytes))
        {
            MaybeDeleteLeftoverSource(blobRoot, row, srcPath, opt);
            return new RewriteResult(RewriteKind.Skipped, sourceBytes.Length, sourceBytes.Length);
        }

        var jpegKey = ToJpegKey(row);
        var thumbKey = ThumbTiers.KeyFor(jpegKey, ThumbTiers.Largest);

        ScreenshotEncodedOriginal encoded;
        try
        {
            await using var input = new MemoryStream(sourceBytes, writable: false);
            encoded = await images.EncodeAsync(input, ToEmbedContext(row), jpegQuality, ct);
        }
        catch (Exception ex)
        {
            return new RewriteResult(
                RewriteKind.Failed, sourceBytes.Length, 0,
                "Encode " + Uuid7Text.Format(row.Id) + ": " + ex.Message);
        }

        if (opt.DryRun)
        {
            return new RewriteResult(RewriteKind.Rewritten, sourceBytes.Length, encoded.Jpeg.Length);
        }

        var jpegPath = ServerPaths.BlobFile(blobRoot, jpegKey);
        try
        {
            await WriteAtomicAsync(jpegPath, encoded.Jpeg, ct);
            foreach (var tier in encoded.Thumbs)
            {
                var tierPath = ServerPaths.BlobFile(blobRoot, ThumbTiers.KeyFor(jpegKey, tier.Size));
                await WriteAtomicAsync(tierPath, tier.Jpeg, ct);
            }
        }
        catch (Exception ex)
        {
            return new RewriteResult(
                RewriteKind.Failed, sourceBytes.Length, 0, "Write " + jpegKey + ": " + ex.Message);
        }

        var sha = Convert.ToHexString(SHA256.HashData(encoded.Jpeg)).ToLowerInvariant();
        try
        {
            await shots.UpdateStoredBlobAsync(
                row.Id, "image/jpeg", encoded.Jpeg.LongLength, encoded.Width, encoded.Height,
                sha, jpegKey, thumbKey, ct);
        }
        catch (Exception ex)
        {
            return new RewriteResult(
                RewriteKind.Failed, sourceBytes.Length, encoded.Jpeg.Length,
                "DB update " + Uuid7Text.Format(row.Id) + ": " + ex.Message);
        }

        if (!opt.KeepSource)
        {
            TryDeleteIfDifferent(srcPath, jpegPath);
            var oldThumb = string.IsNullOrWhiteSpace(row.ThumbStorageKey)
                ? null
                : SafeBlobFile(blobRoot, row.ThumbStorageKey);
            if (oldThumb is not null)
                TryDeleteIfDifferent(oldThumb, ServerPaths.BlobFile(blobRoot, thumbKey));
        }

        return new RewriteResult(RewriteKind.Rewritten, sourceBytes.Length, encoded.Jpeg.Length);
    }

    private static void MaybeDeleteLeftoverSource(
        string blobRoot, ScreenshotRewriteRow row, string jpegPath, ReencodeOptions opt)
    {
        if (opt.KeepSource || opt.DryRun)
            return;
        if (!row.StorageKey.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            return;
        var webpKey = Path.ChangeExtension(row.StorageKey, ".webp")?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(webpKey))
            return;
        var webpPath = SafeBlobFile(blobRoot, webpKey);
        if (webpPath is not null)
            TryDeleteIfDifferent(webpPath, jpegPath);
    }

    private static ScreenshotEmbedContext ToEmbedContext(ScreenshotRewriteRow row) => new()
    {
        ScreenshotId = row.Id,
        UserId = row.UserId,
        UserCode = row.UserCode,
        UserEmail = row.UserEmail,
        UserName = row.UserName,
        MachineId = row.MachineId,
        MachineCode = row.MachineCode,
        MachineName = row.MachineName,
        TimestampUtc = row.TimestampUtc.ToUniversalTime(),
        ClientTimeZoneId = row.ClientTimeZoneId,
        MonitorId = row.MonitorId,
        MonitorName = row.MonitorName,
        WindowTitle = row.WindowTitle,
        ProgramName = row.ProgramName,
        CommandLine = row.CommandLine,
        Notes = row.Notes,
        Tags = row.Tags,
        JournalEntryId = row.JournalEntryId,
        FingerprintJson = row.Fingerprint
    };

    private static string ToJpegKey(ScreenshotRewriteRow row)
    {
        var key = row.StorageKey.Replace('\\', '/');
        var dir = Path.GetDirectoryName(key)?.Replace('\\', '/');
        var file = Uuid7Text.Format(row.Id) + ".jpg";
        if (!string.IsNullOrEmpty(dir))
            return dir.TrimEnd('/') + "/" + file;
        var day = row.TimestampUtc.UtcDateTime;
        return $"{Uuid7Text.Format(row.UserId)}/{day:yyyy}/{day:MM}/{day:dd}/{file}";
    }

    internal static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, path, overwrite: true);
    }

    private static void TryDeleteIfDifferent(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
            return;
        try
        {
            if (File.Exists(oldPath))
                File.Delete(oldPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not delete " + oldPath + ": " + ex.Message);
        }
    }

    private static string? SafeBlobFile(string blobRoot, string key)
    {
        try
        {
            return ServerPaths.BlobFile(blobRoot, key);
        }
        catch
        {
            return null;
        }
    }

    private static ReencodeOptions Parse(string[] args)
    {
        string? root = null;
        string? blobs = null;
        string? cs = null;
        string? provider = null;
        bool dryRun = false;
        bool force = false;
        bool keepSource = false;
        int jobs = 1;
        int? quality = null;
        int? limit = null;
        DateTimeOffset since = DateTimeOffset.UnixEpoch;
        DateTimeOffset until = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? val = null;
            if (a.StartsWith("--", StringComparison.Ordinal) && a.Contains('='))
            {
                int eq = a.IndexOf('=');
                val = a[(eq + 1)..];
                a = a[..eq];
            }

            switch (a)
            {
                case "--root":
                    root = Next(args, ref i, val);
                    break;
                case "--blobs":
                    blobs = Next(args, ref i, val);
                    break;
                case "--connection":
                    cs = Next(args, ref i, val);
                    break;
                case "--provider":
                    provider = Next(args, ref i, val);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--keep-source":
                    keepSource = true;
                    break;
                case "--jobs":
                    jobs = int.Parse(Next(args, ref i, val));
                    break;
                case "--jpeg-quality":
                    quality = int.Parse(Next(args, ref i, val));
                    break;
                case "--limit":
                    limit = int.Parse(Next(args, ref i, val));
                    break;
                case "--since":
                    since = ParseWhen(Next(args, ref i, val));
                    break;
                case "--until":
                    until = ParseWhen(Next(args, ref i, val));
                    break;
                default:
                    throw new ArgumentException("Unknown argument: " + a);
            }
        }

        return new ReencodeOptions
        {
            Root = ServerPaths.ResolveRoot(root),
            BlobRoot = blobs,
            ConnectionString = cs,
            Provider = provider,
            DryRun = dryRun,
            Force = force,
            KeepSource = keepSource,
            Jobs = Math.Clamp(jobs, 1, 16),
            JpegQuality = quality,
            Limit = limit,
            SinceUtc = since.ToUniversalTime(),
            UntilUtc = until.ToUniversalTime()
        };
    }

    internal static DateTimeOffset ParseWhen(string raw)
    {
        if (DateTimeOffset.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dto))
            return dto;
        throw new ArgumentException("Invalid time: " + raw + " (UTC ISO timestamp)");
    }

    internal static string Next(string[] args, ref int i, string? inline)
    {
        if (inline is not null)
            return inline;
        if (i + 1 >= args.Length)
            throw new ArgumentException("Missing value after " + args[i]);
        i++;
        return args[i];
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            schnapp-utils reencode — rewrite stored originals as JPEG with EXIF + XMP.

            Usage:
              schnapp-utils reencode [options]

            Options:
              --root DIR           Schnapp install root (default: /srv/schnapp)
              --blobs DIR          Blob root (default: {root}/data/blobs)
              --connection CS      Catalog connection string
              --provider NAME      PostgreSQL (default) or Firebird
              --dry-run            Encode in memory; do not write files or DB
              --force              Re-encode even when Schnapp XMP is already present
              --keep-source        Leave the previous original (e.g. .webp) in place
              --jobs N             Parallel workers (default: 1)
              --jpeg-quality N     Override screenshot.jpeg_quality (1-100)
              --limit N            Stop after N catalog rows (debug)
              --since ISO          Only shots at/after this UTC time
              --until ISO          Only shots before this UTC time

            Connection is also read from SCHNAPP_CONNECTION or {root}/api/appsettings.json.
            After a successful rewrite the DB storage_key is {id}.jpg and leftover .webp is deleted.
            """);
    }

    private sealed class ReencodeOptions
    {
        public required string Root { get; init; }
        public string? BlobRoot { get; init; }
        public string? ConnectionString { get; init; }
        public string? Provider { get; init; }
        public bool DryRun { get; init; }
        public bool Force { get; init; }
        public bool KeepSource { get; init; }
        public int Jobs { get; init; }
        public int? JpegQuality { get; init; }
        public int? Limit { get; init; }
        public DateTimeOffset SinceUtc { get; init; }
        public DateTimeOffset UntilUtc { get; init; }
    }

    private static void MaybeLogProgress(
        ProgressClock clock,
        int done,
        int total,
        DateTime started,
        int rewritten,
        int skipped,
        int missing,
        int failed,
        long bytesIn,
        long bytesOut,
        bool force)
    {
        var nowTick = Environment.TickCount64;
        var prev = Interlocked.Read(ref clock.LastTick);
        if (!force && done != 1 && done % 100 != 0 && nowTick - prev < 5000)
            return;
        Interlocked.Exchange(ref clock.LastTick, nowTick);

        var elapsed = DateTime.UtcNow - started;
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var rate = done / seconds;
        var pct = total <= 0 ? 0 : 100.0 * done / total;
        var eta = "";
        if (!force && total > done && rate > 0.05)
            eta = "  ETA " + FormatDuration(TimeSpan.FromSeconds((total - done) / rate));

        var atTicks = Interlocked.Read(ref clock.LastAtTicks);
        var at = atTicks > 0
            ? "  " + new DateTimeOffset(atTicks, TimeSpan.Zero).ToString("yyyy-MM-dd HH:mm") + "Z"
            : "";

        lock (Console.Error)
        {
            Console.Error.WriteLine(
                $"reencode {done:N0}/{total:N0} ({pct:0.0}%)  {rate:0.0}/s{eta}  "
                + $"rewritten {rewritten}  skipped {skipped}  missing {missing}  failed {failed}{at}"
                + (rewritten > 0 ? $"  {FormatBytes(bytesIn)} → {FormatBytes(bytesOut)}" : ""));
        }
    }

    internal static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1)
            return $"{t.TotalMinutes:0.0}m";
        return $"{t.TotalSeconds:0.0}s";
    }

    internal static string FormatBytes(long n)
    {
        if (n >= 1_000_000_000)
            return $"{n / 1_000_000_000.0:0.00} GB";
        if (n >= 1_000_000)
            return $"{n / 1_000_000.0:0.0} MB";
        if (n >= 1_000)
            return $"{n / 1_000.0:0.0} KB";
        return n + " B";
    }

    private sealed class ProgressClock
    {
        public long LastTick;
        public long LastAtTicks;
    }

    private readonly record struct RewriteResult(RewriteKind Kind, long BytesIn, long BytesOut, string? Detail = null);

    private enum RewriteKind { Rewritten, Skipped, Missing, Failed }
}
