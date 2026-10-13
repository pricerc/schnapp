namespace Schnapp.Utils;

/// <summary>A run of consecutive similar frames reported as one idle period.</summary>
internal sealed class IdleRange
{
    public required DateTimeOffset StartLocal { get; init; }
    public required DateTimeOffset EndLocal { get; init; }
    public required int FrameCount { get; init; }
    public required int PairCount { get; init; }
    public required double Confidence { get; init; }
    public required string ConfidenceLabel { get; init; }
    public required double NightOverlap { get; init; }
    public required double MeanMetric { get; init; }
    public required double MeanSimilarity { get; init; }
    public required string FirstId { get; init; }
    public required string LastId { get; init; }
}
