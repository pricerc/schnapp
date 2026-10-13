namespace Schnapp.Utils;

/// <summary>
/// Splits the thumbnail into a coarse grid. Idle when only a couple of cells
/// (clock / tray) have a meaningful mean-luma shift.
/// </summary>
internal sealed class GridBlockScorer : IPairScorer
{
    public const int GridCols = 16;
    public const int GridRows = 8;
    public const double CellMeanDelta = 8.0;
    public const int MaxChangedCells = 2;

    public string Name => "Grid / block compare";

    public string Description =>
        $"Idle if at most {MaxChangedCells} of {GridCols}x{GridRows} cells have mean |Δluma| > {CellMeanDelta:0}.";

    public string MetricName => "cells";

    public IReadOnlyList<PairScore> ScoreAll(IReadOnlyList<FrameRecord> frames)
    {
        PairScore[] scores = new PairScore[Math.Max(0, frames.Count - 1)];
        for (int i = 0; i < scores.Length; i++)
        {
            FrameRecord a = frames[i];
            FrameRecord b = frames[i + 1];
            double gap = (b.TimestampUtc - a.TimestampUtc).TotalSeconds;
            if (!ChangedPixelScorer.SameThumb(a, b))
            {
                scores[i] = new PairScore(i, false, 0, GridCols * GridRows, gap);
                continue;
            }

            int changed = CountChangedCells(a, b);
            int total = GridCols * GridRows;
            bool idle = changed <= MaxChangedCells;
            double similarity = 1.0 - (changed / (double)total);
            scores[i] = new PairScore(i, idle, similarity, changed, gap);
        }

        return scores;
    }

    private static int CountChangedCells(FrameRecord a, FrameRecord b)
    {
        int w = a.ThumbWidth;
        int h = a.ThumbHeight;
        int changed = 0;
        for (int gy = 0; gy < GridRows; gy++)
        {
            int y0 = gy * h / GridRows;
            int y1 = (gy + 1) * h / GridRows;
            for (int gx = 0; gx < GridCols; gx++)
            {
                int x0 = gx * w / GridCols;
                int x1 = (gx + 1) * w / GridCols;
                long sum = 0;
                int n = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * w;
                    for (int x = x0; x < x1; x++)
                    {
                        sum += Math.Abs(a.Luma[row + x] - b.Luma[row + x]);
                        n++;
                    }
                }

                if (n > 0 && (sum / (double)n) > CellMeanDelta)
                {
                    changed++;
                }
            }
        }

        return changed;
    }
}
