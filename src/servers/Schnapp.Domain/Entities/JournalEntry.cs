using Medo;

namespace Schnapp.Domain.Entities;

/// <summary>
/// Time-tracking segment for a user on a machine, linked to a journal target.
/// Open entries have a null <see cref="StopUtc"/> (at most one open per user/machine).
/// </summary>
public sealed class JournalEntry
{
    public Uuid7 Id { get; set; }
    public Uuid7 UserId { get; set; }
    public Uuid7 MachineId { get; set; }

    /// <summary>Segment start (UTC).</summary>
    public DateTimeOffset StartUtc { get; set; }

    /// <summary>Segment end (UTC); null while the entry is still open.</summary>
    public DateTimeOffset? StopUtc { get; set; }

    /// <summary>Resolved customer/project/task triple for this segment.</summary>
    public Uuid7 JournalTargetId { get; set; }

    public string? Notes { get; set; }

    /// <summary>Free-text tags for the segment.</summary>
    public string? Tags { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
