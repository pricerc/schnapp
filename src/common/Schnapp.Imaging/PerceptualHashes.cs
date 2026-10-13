namespace Schnapp.Imaging;

/// <summary>64-bit aHash / dHash / pHash from pre-binned luma cells.</summary>
internal static class PerceptualHashes
{
    public static ulong AverageHash(float[] cell8x8)
    {
        float mean = 0;
        for (int i = 0; i < 64; i++)
        {
            mean += cell8x8[i];
        }

        mean /= 64f;
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
        {
            if (cell8x8[i] >= mean)
            {
                hash |= 1UL << i;
            }
        }

        return hash;
    }

    public static ulong DifferenceHash(float[] cell9x8)
    {
        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < 8; y++)
        {
            int row = y * 9;
            for (int x = 0; x < 8; x++)
            {
                if (cell9x8[row + x] > cell9x8[row + x + 1])
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    public static ulong PHash(float[] cell32x32)
    {
        float[] dct = Dct2D(cell32x32, 32);
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

    public static int Hamming(ulong a, ulong b)
    {
        ulong v = a ^ b;
        int c = 0;
        while (v != 0)
        {
            v &= v - 1;
            c++;
        }

        return c;
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
}
