namespace Schnapp.Utils;

/// <summary>Plain-text tables for visual confirmation of detected idle ranges.</summary>
internal static class ReportWriter
{
    public static void WriteMethod(
        IPairScorer scorer,
        IReadOnlyList<FrameRecord> frames,
        IReadOnlyList<PairScore> pairs,
        IReadOnlyList<IdleRange> ranges,
        ScanOptions options,
        TimeSpan elapsed)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 108));
        Console.WriteLine(scorer.Name);
        Console.WriteLine(new string('=', 108));
        Console.WriteLine(scorer.Description);
        Console.WriteLine(
            $"Frames {frames.Count}  pairs {pairs.Count}  scored in {elapsed.TotalSeconds:0.0}s");

        int idlePairs = 0;
        foreach (PairScore p in pairs)
        {
            if (p.Idle)
            {
                idlePairs++;
            }
        }

        double idlePct = pairs.Count == 0 ? 0 : 100.0 * idlePairs / pairs.Count;
        Console.WriteLine($"Idle pairs: {idlePairs}/{pairs.Count} ({idlePct:0.0}%)");
        Console.WriteLine($"Ranges ≥ {options.MinIdleMinutes} min (gap split at {options.MaxGapMinutes} min): {ranges.Count}");
        Console.WriteLine();

        if (ranges.Count == 0)
        {
            Console.WriteLine("(no idle ranges)");
            WriteNightMatrix(frames, ranges, options);
            return;
        }

        Console.WriteLine(
            Pad("#", 4)
            + Pad("Local start", 18)
            + Pad("Local end", 18)
            + Pad("Duration", 10)
            + Pad("Frames", 8)
            + Pad("Conf", 12)
            + Pad("Night", 8)
            + Pad("Mean " + scorer.MetricName, 16)
            + "First id");
        Console.WriteLine(new string('-', 108));

        int n = 1;
        TimeSpan total = TimeSpan.Zero;
        foreach (IdleRange r in ranges)
        {
            TimeSpan dur = r.EndLocal - r.StartLocal;
            total += dur;
            Console.WriteLine(
                Pad(n.ToString(), 4)
                + Pad(Fmt(r.StartLocal), 18)
                + Pad(Fmt(r.EndLocal), 18)
                + Pad(FmtDur(dur), 10)
                + Pad(r.FrameCount.ToString(), 8)
                + Pad($"{r.Confidence:0} {r.ConfidenceLabel}", 12)
                + Pad($"{r.NightOverlap * 100:0}%", 8)
                + Pad(r.MeanMetric.ToString("0.00"), 16)
                + r.FirstId);
            n++;
        }

        Console.WriteLine(new string('-', 108));
        Console.WriteLine($"Total idle time in table: {FmtDur(total)}");
        WriteNightMatrix(frames, ranges, options);
    }

    public static void WriteNightComparison(
        IReadOnlyList<(string Method, IReadOnlyList<IdleRange> Ranges)> methods,
        IReadOnlyList<FrameRecord> frames,
        ScanOptions options,
        string? subtitle = null)
    {
        List<DateOnly> nights = ExpectedNightsWithFrames(frames, options);
        if (nights.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 108));
        Console.WriteLine(
            $"Expected idle window {options.ExpectedStart:HH\\:mm}-{options.ExpectedEnd:HH\\:mm} local — coverage by method"
            + (string.IsNullOrEmpty(subtitle) ? "" : " (" + subtitle + ")"));
        Console.WriteLine(new string('=', 108));

        string header = Pad("Night", 14);
        foreach ((string method, IReadOnlyList<IdleRange> _) in methods)
        {
            header += Pad(ShortName(method), 14);
        }

        Console.WriteLine(header);
        Console.WriteLine(new string('-', 14 + (14 * methods.Count)));

        foreach (DateOnly night in nights)
        {
            string row = Pad(night.ToString("yyyy-MM-dd"), 14);
            foreach ((string _, IReadOnlyList<IdleRange> ranges) in methods)
            {
                double cov = NightCoverage(night, ranges, options);
                row += Pad(cov < 0.005 ? "—" : $"{cov * 100:0}%", 14);
            }

            Console.WriteLine(row);
        }
    }

    private static void WriteNightMatrix(
        IReadOnlyList<FrameRecord> frames,
        IReadOnlyList<IdleRange> ranges,
        ScanOptions options)
    {
        List<DateOnly> nights = ExpectedNightsWithFrames(frames, options);
        if (nights.Count == 0)
        {
            return;
        }

        int full = 0;
        int partial = 0;
        int missed = 0;
        foreach (DateOnly night in nights)
        {
            double cov = NightCoverage(night, ranges, options);
            if (cov >= 0.90)
            {
                full++;
            }
            else if (cov > 0.05)
            {
                partial++;
            }
            else
            {
                missed++;
            }
        }

        Console.WriteLine(
            $"Expected {options.ExpectedStart:HH\\:mm}-{options.ExpectedEnd:HH\\:mm} windows with captures: "
            + $"{nights.Count}  full(≥90%) {full}  partial {partial}  missed {missed}");
    }

    private static List<DateOnly> ExpectedNightsWithFrames(
        IReadOnlyList<FrameRecord> frames,
        ScanOptions options)
    {
        HashSet<DateOnly> set = [];
        foreach (FrameRecord f in frames)
        {
            if (IdleRangeBuilder.IsExpectedIdleHour(f.TimestampLocal, options))
            {
                set.Add(WindowDate(f.TimestampLocal, options));
            }
        }

        List<DateOnly> list = set.ToList();
        list.Sort();
        return list;
    }

    private static DateOnly WindowDate(DateTimeOffset local, ScanOptions options)
    {
        // For a window that does not wrap midnight, the date is the local date.
        // For a wrap (e.g. 22:00-06:00), hours after start belong to that evening's date.
        TimeOnly t = TimeOnly.FromTimeSpan(local.TimeOfDay);
        if (options.ExpectedStart <= options.ExpectedEnd)
        {
            return DateOnly.FromDateTime(local.DateTime);
        }

        if (t >= options.ExpectedStart)
        {
            return DateOnly.FromDateTime(local.DateTime);
        }

        return DateOnly.FromDateTime(local.DateTime).AddDays(-1);
    }

    private static double NightCoverage(DateOnly night, IReadOnlyList<IdleRange> ranges, ScanOptions options)
    {
        DateTime start = night.ToDateTime(options.ExpectedStart);
        DateTime end = options.ExpectedStart <= options.ExpectedEnd
            ? night.ToDateTime(options.ExpectedEnd)
            : night.AddDays(1).ToDateTime(options.ExpectedEnd);
        TimeSpan window = end - start;
        if (window <= TimeSpan.Zero)
        {
            return 0;
        }

        TimeSpan covered = TimeSpan.Zero;
        foreach (IdleRange r in ranges)
        {
            DateTime rs = r.StartLocal.DateTime;
            DateTime re = r.EndLocal.DateTime;
            DateTime a = rs > start ? rs : start;
            DateTime b = re < end ? re : end;
            if (b > a)
            {
                covered += b - a;
            }
        }

        double frac = covered.TotalSeconds / window.TotalSeconds;
        return Math.Clamp(frac, 0, 1);
    }

    private static string ShortName(string name)
    {
        if (name.Contains("changed-pixel", StringComparison.OrdinalIgnoreCase))
        {
            return "pixel";
        }

        if (name.Contains("Grid", StringComparison.OrdinalIgnoreCase))
        {
            return "grid";
        }

        if (name.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome";
        }

        if (name.Contains("aHash", StringComparison.OrdinalIgnoreCase))
        {
            return "aHash";
        }

        if (name.Contains("dHash", StringComparison.OrdinalIgnoreCase))
        {
            return "dHash";
        }

        if (name.Contains("pHash", StringComparison.OrdinalIgnoreCase))
        {
            return "pHash";
        }

        if (name.Contains("black", StringComparison.OrdinalIgnoreCase))
        {
            return "black";
        }

        return name.Length <= 12 ? name : name.Substring(0, 12);
    }

    private static string Fmt(DateTimeOffset t) => t.ToString("yyyy-MM-dd HH:mm");

    private static string FmtDur(TimeSpan t)
    {
        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        }

        return $"{(int)t.TotalMinutes}m";
    }

    private static string Pad(string s, int width)
    {
        if (s.Length >= width)
        {
            return s.Substring(0, width);
        }

        return s + new string(' ', width - s.Length);
    }
}
