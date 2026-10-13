using Schnapp.Data;

namespace Schnapp.Utils;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (args.Length > 0 && IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        if (args.Length > 0 && string.Equals(args[0], "reencode", StringComparison.OrdinalIgnoreCase))
            return await ReencodeCommand.Run(args[1..], cts.Token);

        if (args.Length > 0 && string.Equals(args[0], "thumbs", StringComparison.OrdinalIgnoreCase))
            return await ThumbsCommand.Run(args[1..], cts.Token);

        if (args.Length > 0 && string.Equals(args[0], "dupes", StringComparison.OrdinalIgnoreCase))
            return await RunDupes(args[1..], cts.Token);

        if (args.Length > 0 && string.Equals(args[0], "idle", StringComparison.OrdinalIgnoreCase))
            args = args[1..];
        else if (args.Length > 0 && string.Equals(args[0], "blobs", StringComparison.OrdinalIgnoreCase))
        {
            string[] rest = new string[args.Length];
            rest[0] = "--blobs";
            Array.Copy(args, 1, rest, 1, args.Length - 1);
            args = rest;
        }

        return RunIdle(args);
    }

    private static async Task<int> RunDupes(string[] args, CancellationToken ct)
    {
        ScanOptions options;
        try
        {
            options = ParseArgs(args) with { UseBlobs = true };
            // Unlike idle, dupes covers every monitor unless --monitor is given.
            if (!args.Any(static a => a.StartsWith("--monitor", StringComparison.Ordinal)))
                options = options with { AllMonitors = true };
            options = ResolveBlobIdleOptions(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        return await DupesCommand.Run(options, ct);
    }

    private static int RunIdle(string[] args)
    {
        ScanOptions options;
        try
        {
            options = ParseArgs(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintHelp();
            return 1;
        }

        if (options.UseBlobs)
        {
            try
            {
                options = ResolveBlobIdleOptions(options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        Console.WriteLine("schnapp-utils idle");
        if (options.UseBlobs)
        {
            Console.WriteLine("Source:   server blobs");
            Console.WriteLine("Root:     " + options.Root);
            Console.WriteLine("Blobs:    " + options.BlobRoot);
            Console.WriteLine("Provider: " + options.Provider);
            Console.WriteLine("Catalog:  " + ServerPaths.RedactConnection(options.ConnectionString));
        }
        else
        {
            Console.WriteLine("Source:   local spool");
            Console.WriteLine("Spool:    " + options.SpoolDirectory);
        }

        Console.WriteLine(
            options.AllMonitors
                ? "Monitor:  (all)"
                : "Monitor:  " + options.MonitorName);
        Console.WriteLine(
            $"Thumb:    {options.ThumbWidth}px wide  min idle {options.MinIdleMinutes}m  max gap {options.MaxGapMinutes}m");
        Console.WriteLine(
            $"Expected idle hours (local, for Night column / coverage): {options.ExpectedStart:HH\\:mm}-{options.ExpectedEnd:HH\\:mm}");
        Console.WriteLine("Detectors run on a box-downsampled grayscale thumb, not raw pixels.");
        Console.WriteLine(
            options.ExcludeBlack
                ? "Duplicate methods: all-black lock-screen frames excluded."
                : "Duplicate methods: all-black frames included (--include-black).");
        Console.WriteLine();

        Console.Error.WriteLine(options.UseBlobs ? "Loading catalog…" : "Scanning meta.json…");
        List<SpoolMeta> items = options.UseBlobs ? BlobCatalog.Load(options) : SpoolCatalog.Load(options);
        if (items.Count == 0)
        {
            Console.Error.WriteLine(options.UseBlobs ? "No matching blob items." : "No matching spool items.");
            return 2;
        }

        Console.WriteLine(
            $"Catalogued {items.Count} "
            + (options.AllMonitors ? "frames  " : $"'{options.MonitorName}' frames  ")
            + $"{items[0].TimestampLocal:yyyy-MM-dd HH:mm} → {items[^1].TimestampLocal:yyyy-MM-dd HH:mm} local");
        if (options.SinceUtc is DateTimeOffset sinceBanner)
        {
            Console.WriteLine($"Since UTC: {sinceBanner:yyyy-MM-dd HH:mm:ss}Z");
        }

        Dictionary<string, int> kinds = new(StringComparer.Ordinal);
        foreach (SpoolMeta item in items)
        {
            kinds.TryGetValue(item.WebpKind, out int k);
            kinds[item.WebpKind] = k + 1;
        }

        Console.WriteLine(
            "Image kinds: " + string.Join(", ", kinds.Select(kv => $"{kv.Key} {kv.Value}")));
        Console.WriteLine();

        Console.Error.WriteLine("Decoding originals → grayscale thumbs (once, reused by every method)…");
        DateTime decodeStart = DateTime.UtcNow;
        List<FrameRecord> frames = ThumbCache.Build(items, options.ThumbWidth, options.Progress);
        Console.WriteLine(
            $"Decoded {frames.Count} thumbs in {(DateTime.UtcNow - decodeStart).TotalSeconds:0.0}s  "
            + $"size {frames[0].ThumbWidth}x{frames[0].ThumbHeight}");
        if (frames.Count < 2)
        {
            Console.Error.WriteLine("Need at least two frames.");
            return 2;
        }

        IPairScorer[] scorers = FilterScorers(
        [
            new ChangedPixelScorer(),
            new GridBlockScorer(),
            new AdaptiveChromeScorer(),
            new PerceptualHashScorer(HashKind.Average, maxHamming: 6),
            new PerceptualHashScorer(HashKind.Difference, maxHamming: 8),
            new PerceptualHashScorer(HashKind.PHash, maxHamming: 10),
            new AllBlackScorer()
        ], options.Methods);

        if (scorers.Length == 0)
        {
            Console.Error.WriteLine("No methods selected.");
            return 1;
        }

        List<FrameRecord> contentFrames = new(frames.Count);
        int blackCount = 0;
        foreach (FrameRecord frame in frames)
        {
            if (AllBlackScorer.IsBlack(frame))
            {
                blackCount++;
            }
            else
            {
                contentFrames.Add(frame);
            }
        }

        Console.WriteLine(
            $"All-black frames: {blackCount}/{frames.Count} ({Pct(blackCount, frames.Count)}). "
            + $"Content frames: {contentFrames.Count}.");
        if (options.ExcludeBlack && contentFrames.Count < 2 && scorers.Any(static s => s is not AllBlackScorer))
        {
            Console.Error.WriteLine("Need at least two non-black frames for duplicate methods.");
            return 2;
        }

        List<(string Method, IReadOnlyList<IdleRange> Ranges)> contentComparison = [];
        List<(string Method, IReadOnlyList<IdleRange> Ranges)> blackComparison = [];

        int familyCount = CountFamilies(scorers);
        int methodIndex = 0;
        string? lastFamily = null;
        foreach (IPairScorer scorer in scorers)
        {
            string family = scorer is PerceptualHashScorer ? "hash" : scorer.Name;
            if (family != lastFamily)
            {
                methodIndex++;
                lastFamily = family;
            }

            bool isBlack = scorer is AllBlackScorer;
            var scored = isBlack || !options.ExcludeBlack
                ? frames
                : contentFrames;

            string header = $"Method {methodIndex}/{familyCount} — {scorer.Name}";
            Console.WriteLine();
            Console.WriteLine(header);
            if (!isBlack && options.ExcludeBlack)
            {
                Console.WriteLine($"Scoring {scored.Count} content frames (black lock-screens removed; gaps split ranges).");
            }

            DateTime t0 = DateTime.UtcNow;
            IReadOnlyList<PairScore> pairs = scorer.ScoreAll(scored);
            TimeSpan elapsed = DateTime.UtcNow - t0;
            if (scorer is AllBlackScorer black)
            {
                Console.WriteLine(
                    $"Black frames: {black.BlackFrames}/{scored.Count} "
                    + $"({Pct(black.BlackFrames, scored.Count)})");
            }

            List<IdleRange> ranges = IdleRangeBuilder.Build(scored, pairs, options);
            ReportWriter.WriteMethod(scorer, scored, pairs, ranges, options, elapsed);
            if (isBlack)
            {
                blackComparison.Add((scorer.Name, ranges));
            }
            else
            {
                contentComparison.Add((scorer.Name, ranges));
            }
        }

        if (blackComparison.Count > 0)
        {
            ReportWriter.WriteNightComparison(blackComparison, frames, options, "all-black method, full set");
        }

        if (contentComparison.Count > 0)
        {
            IReadOnlyList<FrameRecord> nightFrames = options.ExcludeBlack ? contentFrames : frames;
            ReportWriter.WriteNightComparison(
                contentComparison,
                nightFrames,
                options,
                options.ExcludeBlack ? "duplicate methods, content frames only" : "duplicate methods, full set");
        }
        Console.WriteLine();
        Console.WriteLine(
            options.UseBlobs
                ? "Done. Open the blob original to visually confirm a range."
                : "Done. Open spool/{id}/image.bin to visually confirm a range.");
        return 0;
    }

    private static ScanOptions ResolveBlobIdleOptions(ScanOptions options)
    {
        var settings = ServerPaths.TryLoadSettings(options.Root);
        var blobRoot = ServerPaths.ResolveBlobRoot(
            options.Root,
            string.IsNullOrWhiteSpace(options.BlobRoot) ? null : options.BlobRoot,
            settings);
        var cs = ServerPaths.ResolveConnectionString(options.Root, options.ConnectionString, settings);
        var provider = ServerPaths.ResolveProvider(options.ProviderName, settings);

        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                "Catalog connection string required (--connection, SCHNAPP_CONNECTION, or appsettings ConnectionStrings:Main).");
        }

        if (!Directory.Exists(blobRoot))
            throw new DirectoryNotFoundException("Blob root not found: " + blobRoot);

        return options with
        {
            BlobRoot = blobRoot,
            ConnectionString = cs,
            Provider = provider
        };
    }

    private static ScanOptions ParseArgs(string[] args)
    {
        string spool = DefaultSpool();
        string monitor = "desktop";
        bool allMonitors = false;
        int thumb = 160;
        int minMinutes = 15;
        int maxGap = 20;
        TimeOnly expectedStart = new(1, 0);
        TimeOnly expectedEnd = new(8, 0);
        int? limit = null;
        string[]? methods = null;
        bool excludeBlack = true;
        DateTimeOffset? sinceUtc = null;
        DateTimeOffset? untilUtc = null;
        bool useBlobs = false;
        string? root = null;
        string? blobRoot = null;
        string? cs = null;
        string? provider = null;
        int maxDirty = 2;
        bool saveFingerprints = true;
        bool progress = false;

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
                case "--spool":
                    spool = Next(args, ref i, val);
                    break;
                case "--monitor":
                    monitor = Next(args, ref i, val);
                    if (monitor is "*" or "all")
                        allMonitors = true;
                    break;
                case "--all-monitors":
                    allMonitors = true;
                    break;
                case "--thumb-width":
                    thumb = int.Parse(Next(args, ref i, val));
                    break;
                case "--min-minutes":
                    minMinutes = int.Parse(Next(args, ref i, val));
                    break;
                case "--max-gap-minutes":
                    maxGap = int.Parse(Next(args, ref i, val));
                    break;
                case "--expected-start":
                    expectedStart = TimeOnly.Parse(Next(args, ref i, val));
                    break;
                case "--expected-end":
                    expectedEnd = TimeOnly.Parse(Next(args, ref i, val));
                    break;
                case "--limit":
                    limit = int.Parse(Next(args, ref i, val));
                    break;
                case "--methods":
                    methods = Next(args, ref i, val)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--include-black":
                    excludeBlack = false;
                    break;
                case "--exclude-black":
                    excludeBlack = true;
                    break;
                case "--since":
                    sinceUtc = ParseWhen(Next(args, ref i, val), relativeToNow: true);
                    break;
                case "--until":
                    untilUtc = ParseWhen(Next(args, ref i, val), relativeToNow: false);
                    break;
                case "--blobs":
                    useBlobs = true;
                    if (val is not null)
                        blobRoot = val;
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        blobRoot = args[++i];
                    break;
                case "--blob-root":
                    useBlobs = true;
                    blobRoot = Next(args, ref i, val);
                    break;
                case "--root":
                    useBlobs = true;
                    root = Next(args, ref i, val);
                    break;
                case "--connection":
                    useBlobs = true;
                    cs = Next(args, ref i, val);
                    break;
                case "--provider":
                    provider = Next(args, ref i, val);
                    break;
                case "--max-dirty":
                    maxDirty = int.Parse(Next(args, ref i, val));
                    break;
                case "--no-save":
                    saveFingerprints = false;
                    break;
                case "--progress":
                    progress = true;
                    break;
                default:
                    throw new ArgumentException("Unknown argument: " + a);
            }
        }

        return new ScanOptions
        {
            SpoolDirectory = Path.GetFullPath(spool),
            MonitorName = monitor,
            AllMonitors = allMonitors,
            ThumbWidth = Math.Clamp(thumb, 32, 640),
            MinIdleMinutes = Math.Clamp(minMinutes, 1, 24 * 60),
            MaxGapMinutes = Math.Clamp(maxGap, 6, 24 * 60),
            ExpectedStart = expectedStart,
            ExpectedEnd = expectedEnd,
            Limit = limit,
            Methods = methods,
            ExcludeBlack = excludeBlack,
            SinceUtc = sinceUtc,
            UntilUtc = untilUtc,
            UseBlobs = useBlobs,
            Root = ServerPaths.ResolveRoot(root),
            BlobRoot = blobRoot ?? "",
            ConnectionString = cs,
            ProviderName = provider,
            MaxDirtyCells = Math.Clamp(maxDirty, 1, 20),
            SaveFingerprints = saveFingerprints,
            Progress = progress
        };
    }

    /// <summary>
    /// Parses an absolute timestamp or a relative duration (<c>2h</c>, <c>90m</c>, <c>30s</c>).
    /// Relative values are subtracted from UTC now when <paramref name="relativeToNow"/> is true.
    /// </summary>
    private static DateTimeOffset ParseWhen(string raw, bool relativeToNow)
    {
        string s = raw.Trim();
        if (s.Length >= 2)
        {
            char unit = s[^1];
            if (unit is 'h' or 'H' or 'm' or 'M' or 's' or 'S'
                && double.TryParse(s[..^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double n))
            {
                TimeSpan span = char.ToLowerInvariant(unit) switch
                {
                    'h' => TimeSpan.FromHours(n),
                    'm' => TimeSpan.FromMinutes(n),
                    _ => TimeSpan.FromSeconds(n)
                };
                return DateTimeOffset.UtcNow - span;
            }
        }

        if (DateTimeOffset.TryParse(
                s,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset dto))
        {
            return dto;
        }

        throw new ArgumentException("Invalid time: " + raw + " (use ISO timestamp or 2h / 90m)");
    }

    private static int CountFamilies(IReadOnlyList<IPairScorer> scorers)
    {
        HashSet<string> families = new(StringComparer.Ordinal);
        foreach (IPairScorer s in scorers)
        {
            families.Add(s is PerceptualHashScorer ? "hash" : s.Name);
        }

        return families.Count;
    }

    private static string Pct(int n, int total)
    {
        return total == 0 ? "0%" : $"{100.0 * n / total:0.0}%";
    }

    private static IPairScorer[] FilterScorers(IPairScorer[] all, string[]? methods)
    {
        if (methods is null || methods.Length == 0)
        {
            return all;
        }

        List<IPairScorer> selected = [];
        foreach (IPairScorer s in all)
        {
            foreach (string m in methods)
            {
                if (s.Name.Contains(m, StringComparison.OrdinalIgnoreCase)
                    || MethodAlias(s).Contains(m, StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add(s);
                    break;
                }
            }
        }

        return selected.ToArray();
    }

    private static string MethodAlias(IPairScorer s)
    {
        if (s is ChangedPixelScorer)
        {
            return "pixel";
        }

        if (s is GridBlockScorer)
        {
            return "grid";
        }

        if (s is AdaptiveChromeScorer)
        {
            return "chrome";
        }

        if (s is PerceptualHashScorer)
        {
            return "hash";
        }

        if (s is AllBlackScorer)
        {
            return "black";
        }

        return s.Name;
    }

    private static string Next(string[] args, ref int i, string? inline)
    {
        if (inline is not null)
        {
            return inline;
        }

        if (i + 1 >= args.Length)
        {
            throw new ArgumentException("Missing value after " + args[i]);
        }

        i++;
        return args[i];
    }

    private static string DefaultSpool()
    {
        string? env = Environment.GetEnvironmentVariable("SCHNAPP_SPOOL_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Schnapp", "spool");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return string.IsNullOrEmpty(xdg)
            ? Path.Combine(home, ".local", "share", "schnapp", "spool")
            : Path.Combine(xdg, "schnapp", "spool");
    }

    private static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "-?";

    private static void PrintHelp()
    {
        Console.WriteLine("""
            schnapp-utils — idle detection against the local spool or server blobs,
            JPEG EXIF+XMP rewrite of stored originals, and thumbnail tier generation.

            Usage:
              schnapp-utils [idle] [options]
              schnapp-utils blobs [options]
              schnapp-utils reencode [options]
              schnapp-utils thumbs [options]
              schnapp-utils dupes [options]

            Idle options:
              --spool DIR              Spool root (default: ~/.local/share/schnapp/spool)
              --blobs [DIR]            Use server catalog + blobs (DIR optional blob root)
              --root DIR               Schnapp install root (implies --blobs; default /srv/schnapp)
              --blob-root DIR          Blob root (implies --blobs)
              --connection CS          Catalog connection string (implies --blobs)
              --provider NAME          PostgreSQL (default) or Firebird
              --monitor NAME           Monitor filter (default: desktop; * or all = every monitor)
              --all-monitors           Do not filter by monitor name
              --thumb-width N          Grayscale working width (default: 160)
              --min-minutes N          Minimum idle range duration to report (default: 15)
              --max-gap-minutes N      Split a range if captures are farther apart (default: 20)
              --expected-start HH:mm   Expected idle window start, local (default: 01:00)
              --expected-end HH:mm     Expected idle window end, local (default: 08:00)
              --limit N                Only the first N matching frames (debug)
              --methods LIST           Comma list: pixel,grid,chrome,hash,black (default: all)
              --exclude-black          Default: drop all-black frames from duplicate methods
              --include-black          Score duplicate methods on the full set (including lock screens)
              --since 2h|ISO           Only frames at/after this time (duration is UTC now minus)
              --until ISO              Only frames before this time
              --progress               Progress with percent, rate and ETA on stderr (idle and dupes)
              -h, --help               This text

            Dupes (catalog + blobs; --root/--connection/--since/--until/--monitor/--limit as above):
              Fingerprints every screenshot (all monitors by default), stores missing fingerprints
              in the catalog, and lists blank / idle duplicates per machine + monitor with the kept
              frame each one matches. Same rules as the web "Mark duplicates". Deletes nothing.
              --max-dirty N            Dirty grid cells still counted as duplicate, 1-20 (default: 2)
              --no-save                Do not write computed fingerprints to the catalog

            Reencode (see also: schnapp-utils reencode --help):
              Rewrites stored originals as JPEG with EXIF + Schnapp XMP and refreshes thumbs.
              Needs the catalog. Default root /srv/schnapp, blobs {root}/data/blobs.
              --dry-run --force --keep-source --jobs N --jpeg-quality N --limit N --since --until

            Thumbs (see also: schnapp-utils thumbs --help):
              Creates the 128/256/384/512/640 px gallery thumbnail tiers under {blobs}/thumbs/ for existing
              screenshots. Only missing tiers are generated unless --force.
              --dry-run --force --keep-legacy --jobs N --progress --limit N --since --until

            Connection is also read from SCHNAPP_CONNECTION or {root}/api/appsettings.json.
            The mounted kit's BlobRoot /opt/schnapp/... is mapped onto --root when that directory exists.

            Methods run in order; each prints its table before the next starts:
              1. Near-duplicate / changed-pixel budget
              2. Grid / block compare
              3. Adaptive chrome mask
              4. Perceptual hash (aHash, then dHash, then pHash)
              5. All-black frame (lock screen)
            """);
    }
}
