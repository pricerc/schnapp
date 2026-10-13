namespace Schnapp.Utils;

/// <summary>Comparison of <c>frames[Index]</c> with <c>frames[Index + 1]</c>.</summary>
internal readonly struct PairScore
{
    public PairScore(int index, bool idle, double similarity, double metric, double gapSeconds)
    {
        Index = index;
        Idle = idle;
        Similarity = similarity;
        Metric = metric;
        GapSeconds = gapSeconds;
    }

    public int Index { get; }
    public bool Idle { get; }
    public double Similarity { get; }
    public double Metric { get; }
    public double GapSeconds { get; }
}
