namespace Schnapp.Utils;

/// <summary>Scores consecutive frame pairs; implementations must not mutate <see cref="FrameRecord.Luma"/>.</summary>
internal interface IPairScorer
{
    string Name { get; }
    string Description { get; }
    string MetricName { get; }
    IReadOnlyList<PairScore> ScoreAll(IReadOnlyList<FrameRecord> frames);
}
