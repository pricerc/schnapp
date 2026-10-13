namespace Schnapp.Imaging;

/// <summary>
/// Compact description of a screenshot computed in one pass over full-resolution pixels
/// (no intermediate thumbnail). Grid bins are source-sized (max 240135).
/// </summary>
public sealed class FrameFingerprint
{
    public const int BlackMaxLuma = 8;
    public const double BlackMeanLuma = 2.0;

    public int Version { get; init; } = GridLayout.Version;
    public int Width { get; init; }
    public int Height { get; init; }
    public int GridCols { get; init; }
    public int GridRows { get; init; }
    public byte[] CellMeans { get; init; } = Array.Empty<byte>();
    public double MeanLuma { get; init; }
    public int MaxLuma { get; init; }
    public ulong AHash { get; init; }
    public ulong DHash { get; init; }
    public ulong PHash { get; init; }

    public bool IsBlack => MaxLuma <= BlackMaxLuma && MeanLuma <= BlackMeanLuma;

    /// <summary>Rebuilds a fingerprint from stored grid metadata (hashes optional / zero).</summary>
    public static FrameFingerprint FromStored(
        int width,
        int height,
        int gridCols,
        int gridRows,
        byte[] cellMeans,
        double meanLuma,
        int maxLuma,
        ulong aHash = 0,
        ulong dHash = 0,
        ulong pHash = 0)
    {
        if (cellMeans is null)
        {
            throw new ArgumentNullException(nameof(cellMeans));
        }

        return new FrameFingerprint
        {
            Version = GridLayout.Version,
            Width = width,
            Height = height,
            GridCols = gridCols,
            GridRows = gridRows,
            CellMeans = cellMeans,
            MeanLuma = meanLuma,
            MaxLuma = maxLuma,
            AHash = aHash,
            DHash = dHash,
            PHash = pHash
        };
    }

    /// <summary>
    /// Box-averages <paramref name="pixels"/> into grid bins and perceptual hashes.
    /// </summary>
    /// <param name="stride">Bytes from the start of one row to the next.</param>
    public static FrameFingerprint FromPixels(
        int width,
        int height,
        byte[] pixels,
        int stride,
        PixelLayout layout)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image must be at least 1×1.");
        }

        if (pixels is null)
        {
            throw new ArgumentNullException(nameof(pixels));
        }

        int bpp = layout == PixelLayout.Gray8 ? 1 : 4;
        if (stride < width * bpp)
        {
            throw new ArgumentException("Stride is smaller than a packed row.", nameof(stride));
        }

        if (pixels.Length < stride * (height - 1) + width * bpp)
        {
            throw new ArgumentException("Pixel buffer is shorter than height×stride.", nameof(pixels));
        }

        int cols = GridLayout.ColsFor(width);
        int rows = GridLayout.RowsFor(height);
        long[] cellSum = new long[cols * rows];
        int[] cellCount = new int[cols * rows];
        long[] aSum = new long[64];
        int[] aCount = new int[64];
        long[] dSum = new long[9 * 8];
        int[] dCount = new int[9 * 8];
        long[] pSum = new long[32 * 32];
        int[] pCount = new int[32 * 32];

        long lumaSum = 0;
        int maxLuma = 0;
        long pixelCount = (long)width * height;

        for (int y = 0; y < height; y++)
        {
            int rowOff = y * stride;
            int gy = y * rows / height;
            int gy8 = y * 8 / height;
            int gy32 = y * 32 / height;
            for (int x = 0; x < width; x++)
            {
                int luma = ReadLuma(pixels, rowOff + x * bpp, layout);
                if (luma > maxLuma)
                {
                    maxLuma = luma;
                }

                lumaSum += luma;

                int gx = x * cols / width;
                int ci = gy * cols + gx;
                cellSum[ci] += luma;
                cellCount[ci]++;

                int gx8 = x * 8 / width;
                int ai = gy8 * 8 + gx8;
                aSum[ai] += luma;
                aCount[ai]++;

                int gx9 = x * 9 / width;
                int di = gy8 * 9 + gx9;
                dSum[di] += luma;
                dCount[di]++;

                int gx32 = x * 32 / width;
                int pi = gy32 * 32 + gx32;
                pSum[pi] += luma;
                pCount[pi]++;
            }
        }

        byte[] means = new byte[cols * rows];
        for (int i = 0; i < means.Length; i++)
        {
            int n = cellCount[i];
            means[i] = n <= 0 ? (byte)0 : (byte)(cellSum[i] / n);
        }

        float[] aCells = ToFloatMeans(aSum, aCount);
        float[] dCells = ToFloatMeans(dSum, dCount);
        float[] pCells = ToFloatMeans(pSum, pCount);

        return new FrameFingerprint
        {
            Version = GridLayout.Version,
            Width = width,
            Height = height,
            GridCols = cols,
            GridRows = rows,
            CellMeans = means,
            MeanLuma = lumaSum / (double)pixelCount,
            MaxLuma = maxLuma,
            AHash = PerceptualHashes.AverageHash(aCells),
            DHash = PerceptualHashes.DifferenceHash(dCells),
            PHash = PerceptualHashes.PHash(pCells)
        };
    }

    /// <summary>
    /// Compares this fingerprint to <paramref name="other"/> (typically the last kept frame).
    /// Grid dimensions must match; otherwise every cell is treated as dirty.
    /// </summary>
    public FingerprintDelta Compare(FrameFingerprint? other)
    {
        if (other is null
            || other.GridCols != GridCols
            || other.GridRows != GridRows
            || other.CellMeans.Length != CellMeans.Length)
        {
            int cells = GridCols * GridRows;
            return new FingerprintDelta(
                dirtyCells: cells,
                totalCells: cells,
                aHashHamming: 64,
                dHashHamming: 64,
                pHashHamming: 64);
        }

        int dirty = 0;
        for (int i = 0; i < CellMeans.Length; i++)
        {
            if (Math.Abs(CellMeans[i] - other.CellMeans[i]) > GridLayout.CellMeanDelta)
            {
                dirty++;
            }
        }

        return new FingerprintDelta(
            dirty,
            CellMeans.Length,
            PerceptualHashes.Hamming(AHash, other.AHash),
            PerceptualHashes.Hamming(DHash, other.DHash),
            PerceptualHashes.Hamming(PHash, other.PHash));
    }

    /// <summary>True when dirty cells are within the caller's slider (clamped to this grid).</summary>
    public bool IsIdleDuplicateOf(FrameFingerprint? other, int maxDirtyCells)
    {
        FingerprintDelta delta = Compare(other);
        int cap = GridLayout.ClampMaxDirtyCells(maxDirtyCells, GridCols, GridRows);
        return delta.DirtyCells <= cap;
    }

    private static int ReadLuma(byte[] pixels, int offset, PixelLayout layout)
    {
        switch (layout)
        {
            case PixelLayout.Gray8:
                return pixels[offset];
            case PixelLayout.Bgra32:
                return (77 * pixels[offset + 2] + 150 * pixels[offset + 1] + 29 * pixels[offset]) >> 8;
            default:
                return (77 * pixels[offset] + 150 * pixels[offset + 1] + 29 * pixels[offset + 2]) >> 8;
        }
    }

    private static float[] ToFloatMeans(long[] sum, int[] count)
    {
        float[] dest = new float[sum.Length];
        for (int i = 0; i < sum.Length; i++)
        {
            int n = count[i];
            dest[i] = n <= 0 ? 0 : (float)(sum[i] / (double)n);
        }

        return dest;
    }
}

/// <summary>Result of comparing two <see cref="FrameFingerprint"/> values.</summary>
public readonly struct FingerprintDelta
{
    public FingerprintDelta(int dirtyCells, int totalCells, int aHashHamming, int dHashHamming, int pHashHamming)
    {
        DirtyCells = dirtyCells;
        TotalCells = totalCells;
        AHashHamming = aHashHamming;
        DHashHamming = dHashHamming;
        PHashHamming = pHashHamming;
    }

    public int DirtyCells { get; }
    public int TotalCells { get; }
    public int AHashHamming { get; }
    public int DHashHamming { get; }
    public int PHashHamming { get; }
}
