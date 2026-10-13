namespace Schnapp.Utils;

/// <summary>Turns consecutive idle pair scores into duration-filtered ranges with a confidence rating.</summary>
internal static class IdleRangeBuilder
{
    public static List<IdleRange> Build(
        IReadOnlyList<FrameRecord> frames,
        IReadOnlyList<PairScore> pairs,
        ScanOptions options)
    {
        List<IdleRange> ranges = [];
        if (frames.Count < 2 || pairs.Count == 0)
        {
            return ranges;
        }

        double maxGap = options.MaxGapMinutes * 60.0;
        TimeSpan minDur = TimeSpan.FromMinutes(options.MinIdleMinutes);
        int i = 0;
        while (i < pairs.Count)
        {
            if (!pairs[i].Idle || pairs[i].GapSeconds > maxGap)
            {
                i++;
                continue;
            }

            int start = i;
            i++;
            while (i < pairs.Count && pairs[i].Idle && pairs[i].GapSeconds <= maxGap)
            {
                i++;
            }

            int endPair = i - 1;
            FrameRecord first = frames[start];
            FrameRecord last = frames[endPair + 1];
            if (last.TimestampLocal - first.TimestampLocal < minDur)
            {
                continue;
            }

            ranges.Add(MakeRange(frames, pairs, start, endPair, options));
        }

        return ranges;
    }

    private static IdleRange MakeRange(
        IReadOnlyList<FrameRecord> frames,
        IReadOnlyList<PairScore> pairs,
        int start,
        int endPair,
        ScanOptions options)
    {
        int pairCount = endPair - start + 1;
        double sumSim = 0;
        double sumMetric = 0;
        double sumSq = 0;
        int nightFrames = 0;
        int frameCount = pairCount + 1;

        for (int p = start; p <= endPair; p++)
        {
            sumSim += pairs[p].Similarity;
            sumMetric += pairs[p].Metric;
            double s = pairs[p].Similarity;
            sumSq += s * s;
        }

        for (int f = start; f <= endPair + 1; f++)
        {
            if (IsExpectedIdleHour(frames[f].TimestampLocal, options))
            {
                nightFrames++;
            }
        }

        double meanSim = sumSim / pairCount;
        double meanMetric = sumMetric / pairCount;
        double variance = Math.Max(0, (sumSq / pairCount) - (meanSim * meanSim));
        double std = Math.Sqrt(variance);
        double consistency = 1.0 - Math.Clamp(std / 0.25, 0, 1);
        double hours = (frames[endPair + 1].TimestampLocal - frames[start].TimestampLocal).TotalHours;
        double lengthBoost = Math.Clamp(hours / 3.0, 0, 1);
        // WebP q90 is lossy; never claim 100% even on a visually frozen desktop.
        const double webpCeiling = 0.97;
        double confidence = 100.0 * webpCeiling * (
            (0.65 * meanSim) + (0.25 * consistency) + (0.10 * lengthBoost));
        confidence = Math.Clamp(confidence, 0, 97);

        string label = confidence >= 85 ? "High" : confidence >= 70 ? "Medium" : "Low";

        return new IdleRange
        {
            StartLocal = frames[start].TimestampLocal,
            EndLocal = frames[endPair + 1].TimestampLocal,
            FrameCount = frameCount,
            PairCount = pairCount,
            Confidence = confidence,
            ConfidenceLabel = label,
            NightOverlap = nightFrames / (double)frameCount,
            MeanMetric = meanMetric,
            MeanSimilarity = meanSim,
            FirstId = frames[start].Id,
            LastId = frames[endPair + 1].Id
        };
    }

    public static bool IsExpectedIdleHour(DateTimeOffset local, ScanOptions options)
    {
        TimeOnly t = TimeOnly.FromTimeSpan(local.TimeOfDay);
        TimeOnly start = options.ExpectedStart;
        TimeOnly end = options.ExpectedEnd;
        if (start <= end)
        {
            return t >= start && t < end;
        }

        return t >= start || t < end;
    }
}
