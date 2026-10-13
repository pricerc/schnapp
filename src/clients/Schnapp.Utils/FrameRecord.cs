namespace Schnapp.Utils;

/// <summary>One desktop spool capture plus a grayscale thumbnail used by every detector.</summary>
internal sealed class FrameRecord
{
    public required string Id { get; init; }
    public required string Directory { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required DateTimeOffset TimestampLocal { get; init; }
    public required string TimeZoneId { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public long ByteSize { get; init; }
    public required byte[] Luma { get; init; }
    public int ThumbWidth { get; init; }
    public int ThumbHeight { get; init; }
    public string WebpKind { get; init; } = "";
}
