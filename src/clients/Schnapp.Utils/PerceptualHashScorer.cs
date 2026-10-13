namespace Schnapp.Utils;

/// <summary>One of aHash / dHash / pHash over the shared thumbnail.</summary>
internal enum HashKind
{
    Average,
    Difference,
    PHash
}

/// <summary>64-bit perceptual hash; idle when Hamming distance is small.</summary>
internal sealed class PerceptualHashScorer : IPairScorer
{
    private readonly HashKind _kind;
    private readonly int _maxHamming;

    public PerceptualHashScorer(HashKind kind, int maxHamming)
    {
        _kind = kind;
        _maxHamming = maxHamming;
    }

    public string Name => "Perceptual hash (" + KindLabel + ")";

    public string Description =>
        $"Idle if {KindLabel} Hamming distance ≤ {_maxHamming} / 64. Hash is computed from the 160px grayscale thumb.";

    public string MetricName => "Hamming";

    private string KindLabel => _kind switch
    {
        HashKind.Average => "aHash",
        HashKind.Difference => "dHash",
        HashKind.PHash => "pHash",
        _ => _kind.ToString()
    };

    public IReadOnlyList<PairScore> ScoreAll(IReadOnlyList<FrameRecord> frames)
    {
        ulong[] hashes = new ulong[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            hashes[i] = Compute(frames[i]);
        }

        PairScore[] scores = new PairScore[Math.Max(0, frames.Count - 1)];
        for (int i = 0; i < scores.Length; i++)
        {
            double gap = (frames[i + 1].TimestampUtc - frames[i].TimestampUtc).TotalSeconds;
            int ham = Hamming(hashes[i], hashes[i + 1]);
            bool idle = ham <= _maxHamming;
            double similarity = 1.0 - (ham / 64.0);
            scores[i] = new PairScore(i, idle, similarity, ham, gap);
        }

        return scores;
    }

    private ulong Compute(FrameRecord frame)
    {
        return _kind switch
        {
            HashKind.Average => AverageHash(frame),
            HashKind.Difference => DifferenceHash(frame),
            _ => PHash(frame)
        };
    }

    private static ulong AverageHash(FrameRecord frame)
    {
        float[] cell = BoxReduce(frame, 8, 8);
        float mean = 0;
        for (int i = 0; i < cell.Length; i++)
        {
            mean += cell[i];
        }

        mean /= cell.Length;
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
        {
            if (cell[i] >= mean)
            {
                hash |= 1UL << i;
            }
        }

        return hash;
    }

    private static ulong DifferenceHash(FrameRecord frame)
    {
        float[] cell = BoxReduce(frame, 9, 8);
        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < 8; y++)
        {
            int row = y * 9;
            for (int x = 0; x < 8; x++)
            {
                if (cell[row + x] > cell[row + x + 1])
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    private static ulong PHash(FrameRecord frame)
    {
        float[] pixels = BoxReduce(frame, 32, 32);
        float[] dct = Dct2D(pixels, 32);
        // 8x8 low frequencies, skip DC at [0].
        float[] ac = new float[64];
        int n = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                ac[n++] = dct[(y * 32) + x];
            }
        }

        float[] sorted = (float[])ac.Clone();
        Array.Sort(sorted);
        float median = (sorted[31] + sorted[32]) * 0.5f;
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
        {
            if (ac[i] > median)
            {
                hash |= 1UL << i;
            }
        }

        return hash;
    }

    private static float[] BoxReduce(FrameRecord frame, int dw, int dh)
    {
        int sw = frame.ThumbWidth;
        int sh = frame.ThumbHeight;
        byte[] src = frame.Luma;
        float[] dest = new float[dw * dh];
        for (int y = 0; y < dh; y++)
        {
            int y0 = y * sh / dh;
            int y1 = Math.Max(y0 + 1, (y + 1) * sh / dh);
            for (int x = 0; x < dw; x++)
            {
                int x0 = x * sw / dw;
                int x1 = Math.Max(x0 + 1, (x + 1) * sw / dw);
                long sum = 0;
                int count = 0;
                for (int sy = y0; sy < y1; sy++)
                {
                    int row = sy * sw;
                    for (int sx = x0; sx < x1; sx++)
                    {
                        sum += src[row + sx];
                        count++;
                    }
                }

                dest[(y * dw) + x] = count == 0 ? 0 : (float)(sum / (double)count);
            }
        }

        return dest;
    }

    private static float[] Dct2D(float[] src, int n)
    {
        float[] cos = new float[n * n];
        for (int k = 0; k < n; k++)
        {
            for (int i = 0; i < n; i++)
            {
                cos[(k * n) + i] = (float)Math.Cos(Math.PI * (2 * i + 1) * k / (2.0 * n));
            }
        }

        float[] tmp = new float[n * n];
        float[] dest = new float[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int k = 0; k < n; k++)
            {
                float sum = 0;
                int row = y * n;
                int crow = k * n;
                for (int i = 0; i < n; i++)
                {
                    sum += src[row + i] * cos[crow + i];
                }

                float ck = k == 0 ? 0.70710678f : 1f;
                tmp[row + k] = sum * ck;
            }
        }

        for (int x = 0; x < n; x++)
        {
            for (int k = 0; k < n; k++)
            {
                float sum = 0;
                int crow = k * n;
                for (int i = 0; i < n; i++)
                {
                    sum += tmp[(i * n) + x] * cos[crow + i];
                }

                float ck = k == 0 ? 0.70710678f : 1f;
                dest[(k * n) + x] = sum * ck;
            }
        }

        return dest;
    }

    private static int Hamming(ulong a, ulong b)
    {
        return System.Numerics.BitOperations.PopCount(a ^ b);
    }
}
