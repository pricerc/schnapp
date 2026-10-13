using Medo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schnapp.Contracts;
using Schnapp.Data;
using Schnapp.Data.Repositories;
using Schnapp.Screenshots;

namespace Schnapp.Utils;

/// <summary>
/// Creates (or re-creates) the gallery thumbnail tiers for existing screenshots from their stored originals.
/// </summary>
internal static class ThumbsCommand
{
    public static async Task<int> Run(string[] args, CancellationToken ct)
    {
        if (args.Any(static a => a is "-h" or "--help" or "help" or "-?"))
        {
            PrintHelp();
            return 0;
        }

        ThumbsOptions opt;
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

        Console.WriteLine("schnapp-utils thumbs");
        Console.WriteLine("Root:     " + opt.Root);
        Console.WriteLine("Blobs:    " + blobRoot);
        Console.WriteLine("Provider: " + provider);
        Console.WriteLine("Catalog:  " + ServerPaths.RedactConnection(cs));
        Console.WriteLine(
            $"Range:    {opt.SinceUtc:yyyy-MM-dd HH:mm}Z → {opt.UntilUtc:yyyy-MM-dd HH:mm}Z  jobs={opt.Jobs}  "
            + (opt.DryRun ? "DRY-RUN" : "write")
            + (opt.Force ? "  force" : ""));
        Console.WriteLine($"Tiers:    {string.Join(", ", ThumbTiers.Sizes)}");
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
        var images = scope.ServiceProvider.GetRequiredService<ScreenshotImageProcessor>();

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

        var generated = 0;
        var upToDate = 0;
        var missing = 0;
        var failed = 0;
        var filesWritten = 0;
        var bytesWritten = 0L;
        var done = 0;
        var errorsLogged = 0;
        var progress = new ProgressReporter("thumbs", planned, opt.Progress);
        DateTimeOffset? cursorTs = null;
        Uuid7? cursorId = null;
        var started = DateTime.UtcNow;
        Console.Error.WriteLine("Generating…");

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
                    var (kind, files, bytes, error) = await ProcessAsync(row, blobRoot, images, shots, opt, token);
                    if (error is not null && Interlocked.Increment(ref errorsLogged) <= 8)
                    {
                        lock (Console.Error)
                            Console.Error.WriteLine(error);
                    }

                    switch (kind)
                    {
                        case Outcome.Generated:
                            Interlocked.Increment(ref generated);
                            Interlocked.Add(ref filesWritten, files);
                            Interlocked.Add(ref bytesWritten, bytes);
                            break;
                        case Outcome.UpToDate:
                            Interlocked.Increment(ref upToDate);
                            break;
                        case Outcome.Missing:
                            Interlocked.Increment(ref missing);
                            break;
                        default:
                            Interlocked.Increment(ref failed);
                            break;
                    }

                    Interlocked.Increment(ref done);
                    progress.Tick(failed: kind is Outcome.Failed or Outcome.Missing);
                });

                if (opt.Limit is int cap && done >= cap)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Done in {ReencodeCommand.FormatDuration(DateTime.UtcNow - started)}  generated {generated}  "
            + $"up to date {upToDate}  missing {missing}  failed {failed}");
        if (filesWritten > 0)
        {
            Console.WriteLine(
                $"{(opt.DryRun ? "Would write" : "Wrote")} {filesWritten:N0} thumbs, "
                + ReencodeCommand.FormatBytes(bytesWritten) + ".");
        }

        return failed > 0 ? 2 : 0;
    }

    private static async Task<(Outcome Kind, int Files, long Bytes, string? Error)> ProcessAsync(
        ScreenshotRewriteRow row,
        string blobRoot,
        ScreenshotImageProcessor images,
        ScreenshotRepository shots,
        ThumbsOptions opt,
        CancellationToken ct)
    {
        string srcPath;
        try
        {
            srcPath = ServerPaths.BlobFile(blobRoot, row.StorageKey);
        }
        catch (Exception ex)
        {
            return (Outcome.Failed, 0, 0, Uuid7Text.Format(row.Id) + " bad key: " + ex.Message);
        }

        var tierPaths = ThumbTiers.Sizes.ToDictionary(
            static s => s,
            s => ServerPaths.BlobFile(blobRoot, ThumbTiers.KeyFor(row.StorageKey, s)));
        var needed = opt.Force
            ? ThumbTiers.Sizes.ToList()
            : ThumbTiers.Sizes.Where(s => !File.Exists(tierPaths[s])).ToList();

        var files = 0;
        long bytes = 0;
        try
        {
            if (needed.Count > 0)
            {
                if (!File.Exists(srcPath))
                    return (Outcome.Missing, 0, 0, "Missing " + row.StorageKey);

                await using var input = new FileStream(
                    srcPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                var thumbs = await images.CreateThumbsAsync(input, needed, ct);
                foreach (var t in thumbs)
                {
                    if (!opt.DryRun)
                        await ReencodeCommand.WriteAtomicAsync(tierPaths[t.Size], t.Jpeg, ct);
                    files++;
                    bytes += t.Jpeg.Length;
                }
            }

            var largestKey = ThumbTiers.KeyFor(row.StorageKey, ThumbTiers.Largest);
            if (!opt.DryRun && row.ThumbStorageKey != largestKey)
            {
                await shots.UpdateThumbStorageKeyAsync(row.Id, largestKey, ct);
                if (!opt.KeepLegacy && !string.IsNullOrWhiteSpace(row.ThumbStorageKey))
                {
                    var legacy = ServerPaths.BlobFile(blobRoot, row.ThumbStorageKey);
                    if (File.Exists(legacy))
                        File.Delete(legacy);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (Outcome.Failed, files, bytes, "Thumbs " + Uuid7Text.Format(row.Id) + ": " + ex.Message);
        }

        return (needed.Count > 0 ? Outcome.Generated : Outcome.UpToDate, files, bytes, null);
    }

    private static ThumbsOptions Parse(string[] args)
    {
        string? root = null;
        string? blobs = null;
        string? cs = null;
        string? provider = null;
        var dryRun = false;
        var force = false;
        var keepLegacy = false;
        var progress = false;
        var jobs = Math.Max(1, Environment.ProcessorCount / 2);
        int? limit = null;
        var since = DateTimeOffset.UnixEpoch;
        var until = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? val = null;
            if (a.StartsWith("--", StringComparison.Ordinal) && a.Contains('='))
            {
                var eq = a.IndexOf('=');
                val = a[(eq + 1)..];
                a = a[..eq];
            }

            switch (a)
            {
                case "--root": root = ReencodeCommand.Next(args, ref i, val); break;
                case "--blobs": blobs = ReencodeCommand.Next(args, ref i, val); break;
                case "--connection": cs = ReencodeCommand.Next(args, ref i, val); break;
                case "--provider": provider = ReencodeCommand.Next(args, ref i, val); break;
                case "--dry-run": dryRun = true; break;
                case "--force": force = true; break;
                case "--keep-legacy": keepLegacy = true; break;
                case "--progress": progress = true; break;
                case "--jobs": jobs = int.Parse(ReencodeCommand.Next(args, ref i, val)); break;
                case "--limit": limit = int.Parse(ReencodeCommand.Next(args, ref i, val)); break;
                case "--since": since = ReencodeCommand.ParseWhen(ReencodeCommand.Next(args, ref i, val)); break;
                case "--until": until = ReencodeCommand.ParseWhen(ReencodeCommand.Next(args, ref i, val)); break;
                default: throw new ArgumentException("Unknown argument: " + a);
            }
        }

        return new ThumbsOptions
        {
            Root = ServerPaths.ResolveRoot(root),
            BlobRoot = blobs,
            ConnectionString = cs,
            Provider = provider,
            DryRun = dryRun,
            Force = force,
            KeepLegacy = keepLegacy,
            Progress = progress,
            Jobs = Math.Clamp(jobs, 1, 16),
            Limit = limit,
            SinceUtc = since.ToUniversalTime(),
            UntilUtc = until.ToUniversalTime()
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            schnapp-utils thumbs — create or re-create gallery thumbnail tiers from stored originals.

            Usage:
              schnapp-utils thumbs [options]

            Writes one JPEG per tier (128, 256, 384, 512, 640 px longest edge) to
            {blobs}/thumbs/{size}/{original key without extension}.jpg, then points the catalog
            thumb_storage_key at the 640 tier and removes the old {id}.thumb.jpg.

            Options:
              --root DIR           Schnapp install root (default: /srv/schnapp)
              --blobs DIR          Blob root (default: {root}/data/blobs)
              --connection CS      Catalog connection string
              --provider NAME      PostgreSQL (default) or Firebird
              --force              Re-create tiers that already exist
              --keep-legacy        Leave old {id}.thumb.jpg files in place
              --dry-run            Decode and encode, but write no files and change no DB rows
              --jobs N             Parallel workers (default: half the CPUs)
              --progress           Percent, rate and ETA on stderr
              --limit N            Stop after N catalog rows (debug)
              --since ISO          Only shots at/after this UTC time
              --until ISO          Only shots before this UTC time

            Without --force only missing tiers are generated, so the command is safe to re-run.
            Connection is also read from SCHNAPP_CONNECTION or {root}/api/appsettings.json.
            """);
    }

    private enum Outcome { Generated, UpToDate, Missing, Failed }

    private sealed class ThumbsOptions
    {
        public required string Root { get; init; }
        public string? BlobRoot { get; init; }
        public string? ConnectionString { get; init; }
        public string? Provider { get; init; }
        public bool DryRun { get; init; }
        public bool Force { get; init; }
        public bool KeepLegacy { get; init; }
        public bool Progress { get; init; }
        public int Jobs { get; init; }
        public int? Limit { get; init; }
        public DateTimeOffset SinceUtc { get; init; }
        public DateTimeOffset UntilUtc { get; init; }
    }
}
