namespace Schnapp.Utils;

/// <summary>
/// Learns a small "always changing" mask (clock-like chrome) from a leaky per-pixel
/// change frequency, then scores idle on the unmasked remainder.
/// </summary>
internal sealed class AdaptiveChromeScorer : IPairScorer
{
    public const int DeltaThreshold = 12;
    public const double IdleFraction = 0.012;
    public const int WarmupPairs = 8;
    public const double MaxMaskFraction = 0.03;
    public const double MaskFreq = 0.55;
    public const double Decay = 0.90;
    public const double Learn = 0.10;

    public string Name => "Adaptive chrome mask";

    public string Description =>
        $"Leaky per-pixel change map; mask up to {MaxMaskFraction:P0} of pixels with freq ≥ {MaskFreq:0.00}. "
        + $"Idle if unmasked Δpx ≤ {IdleFraction:P1} after {WarmupPairs} warmup pairs.";

    public string MetricName => "unmasked Δpx %";

    public IReadOnlyList<PairScore> ScoreAll(IReadOnlyList<FrameRecord> frames)
    {
        PairScore[] scores = new PairScore[Math.Max(0, frames.Count - 1)];
        if (scores.Length == 0)
        {
            return scores;
        }

        float[]? freq = null;
        bool[]? mask = null;

        for (int i = 0; i < scores.Length; i++)
        {
            FrameRecord a = frames[i];
            FrameRecord b = frames[i + 1];
            double gap = (b.TimestampUtc - a.TimestampUtc).TotalSeconds;
            if (!ChangedPixelScorer.SameThumb(a, b))
            {
                freq = null;
                mask = null;
                scores[i] = new PairScore(i, false, 0, 100, gap);
                continue;
            }

            int n = a.Luma.Length;
            if (freq is null || freq.Length != n)
            {
                freq = new float[n];
                mask = new bool[n];
            }

            byte[] la = a.Luma;
            byte[] lb = b.Luma;
            int rawChanged = 0;
            for (int p = 0; p < n; p++)
            {
                bool ch = Math.Abs(la[p] - lb[p]) > DeltaThreshold;
                if (ch)
                {
                    rawChanged++;
                }

                freq[p] = (float)((freq[p] * Decay) + (ch ? Learn : 0));
            }

            UpdateMask(freq, mask!, MaxMaskFraction);

            int unmaskedChanged = 0;
            int unmasked = 0;
            for (int p = 0; p < n; p++)
            {
                if (mask![p])
                {
                    continue;
                }

                unmasked++;
                if (Math.Abs(la[p] - lb[p]) > DeltaThreshold)
                {
                    unmaskedChanged++;
                }
            }

            double frac = unmasked > 0 ? unmaskedChanged / (double)unmasked : rawChanged / (double)n;
            bool warmed = i >= WarmupPairs;
            bool idle = warmed && frac <= IdleFraction;
            double similarity = 1.0 - Math.Clamp(frac / Math.Max(IdleFraction * 4, 1e-6), 0, 1);
            if (!warmed)
            {
                similarity *= 0.5;
            }

            scores[i] = new PairScore(i, idle, similarity, frac * 100.0, gap);
        }

        return scores;
    }

    private static void UpdateMask(float[] freq, bool[] mask, double maxFraction)
    {
        int n = freq.Length;
        int cap = Math.Max(1, (int)Math.Floor(n * maxFraction));
        int candidates = 0;
        for (int i = 0; i < n; i++)
        {
            if (freq[i] >= MaskFreq)
            {
                candidates++;
            }
        }

        if (candidates <= cap)
        {
            for (int i = 0; i < n; i++)
            {
                mask[i] = freq[i] >= MaskFreq;
            }

            return;
        }

        // Keep the hottest cap pixels only so a video cannot become "the chrome".
        float[] copy = (float[])freq.Clone();
        Array.Sort(copy);
        float cutoff = copy[n - cap];
        int kept = 0;
        for (int i = 0; i < n; i++)
        {
            bool on = freq[i] >= cutoff && kept < cap;
            mask[i] = on;
            if (on)
            {
                kept++;
            }
        }
    }
}
