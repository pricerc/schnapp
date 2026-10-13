namespace Schnapp.Utils;

/// <summary>
/// Near-duplicate: fraction of thumbnail pixels whose luma moved by more than
/// <see cref="DeltaThreshold"/> (WebP q90 noise floor).
/// </summary>
internal sealed class ChangedPixelScorer : IPairScorer
{
    public const int DeltaThreshold = 12;
    public const double IdleFraction = 0.015;

    public string Name => "Near-duplicate / changed-pixel budget";

    public string Description =>
        $"Idle if < {IdleFraction:P1} of thumbnail pixels move by more than {DeltaThreshold} luma (WebP q90; raw frames would be cleaner).";

    public string MetricName => "Δpx %";

    private static string ThumbHint => "thumb";

    public IReadOnlyList<PairScore> ScoreAll(IReadOnlyList<FrameRecord> frames)
    {
        PairScore[] scores = new PairScore[Math.Max(0, frames.Count - 1)];
        for (int i = 0; i < scores.Length; i++)
        {
            FrameRecord a = frames[i];
            FrameRecord b = frames[i + 1];
            double gap = (b.TimestampUtc - a.TimestampUtc).TotalSeconds;
            if (!SameThumb(a, b))
            {
                scores[i] = new PairScore(i, idle: false, similarity: 0, metric: 100, gap);
                continue;
            }

            int changed = 0;
            byte[] la = a.Luma;
            byte[] lb = b.Luma;
            for (int p = 0; p < la.Length; p++)
            {
                if (Math.Abs(la[p] - lb[p]) > DeltaThreshold)
                {
                    changed++;
                }
            }

            double frac = changed / (double)la.Length;
            bool idle = frac <= IdleFraction;
            double similarity = 1.0 - Math.Clamp(frac / Math.Max(IdleFraction * 4, 1e-6), 0, 1);
            scores[i] = new PairScore(i, idle, similarity, frac * 100.0, gap);
        }

        return scores;
    }

    internal static bool SameThumb(FrameRecord a, FrameRecord b)
    {
        return a.ThumbWidth == b.ThumbWidth
            && a.ThumbHeight == b.ThumbHeight
            && a.Luma.Length == b.Luma.Length;
    }
}
