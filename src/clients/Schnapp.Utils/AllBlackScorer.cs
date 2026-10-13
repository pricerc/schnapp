namespace Schnapp.Utils;

/// <summary>
/// Per-frame "this capture is a black lock screen" check. A pair is idle when
/// both frames are black, so ranges are locked-and-stayed-locked rather than
/// the transition into the lock.
/// </summary>
/// <remarks>
/// WebP q90 of a 5120x1989 solid black desktop in this spool is exactly luma 0
/// after decode (and a constant 17312-byte file). Thresholds allow a little
/// encoder noise so a different encoder or size still matches.
/// </remarks>
internal sealed class AllBlackScorer : IPairScorer
{
    public const int MaxLuma = 8;
    public const double MeanLuma = 2.0;

    public string Name => "All-black frame";

    public string Description =>
        $"Idle when both frames in a pair are black (max luma ≤ {MaxLuma}, mean luma ≤ {MeanLuma:0}). "
        + "This is a per-frame lock-screen check, not a duplicate comparison.";

    public string MetricName => "mean luma";

    /// <summary>Frames classified black on the last <see cref="ScoreAll"/> call.</summary>
    public int BlackFrames { get; private set; }

    public IReadOnlyList<PairScore> ScoreAll(IReadOnlyList<FrameRecord> frames)
    {
        bool[] black = new bool[frames.Count];
        double[] mean = new double[frames.Count];
        int nBlack = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            Summarize(frames[i], out int max, out double avg);
            mean[i] = avg;
            black[i] = max <= MaxLuma && avg <= MeanLuma;
            if (black[i])
            {
                nBlack++;
            }
        }

        BlackFrames = nBlack;

        PairScore[] scores = new PairScore[Math.Max(0, frames.Count - 1)];
        for (int i = 0; i < scores.Length; i++)
        {
            double gap = (frames[i + 1].TimestampUtc - frames[i].TimestampUtc).TotalSeconds;
            bool idle = black[i] && black[i + 1];
            double similarity = idle
                ? 1.0 - Math.Clamp(Math.Max(mean[i], mean[i + 1]) / 16.0, 0, 1)
                : 0;
            scores[i] = new PairScore(i, idle, similarity, mean[i + 1], gap);
        }

        return scores;
    }

    internal static bool IsBlack(FrameRecord frame)
    {
        Summarize(frame, out int max, out double mean);
        return max <= MaxLuma && mean <= MeanLuma;
    }

    private static void Summarize(FrameRecord frame, out int max, out double mean)
    {
        byte[] luma = frame.Luma;
        int m = 0;
        long sum = 0;
        for (int i = 0; i < luma.Length; i++)
        {
            int v = luma[i];
            sum += v;
            if (v > m)
            {
                m = v;
            }
        }

        max = m;
        mean = luma.Length == 0 ? 0 : sum / (double)luma.Length;
    }
}
