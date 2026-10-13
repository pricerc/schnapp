namespace Schnapp.Contracts;

using Medo;

/// <summary>Journal entry returned by the API.</summary>
public sealed class JournalEntryResponse
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
    public string? Tags { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>True when <see cref="StopUtc"/> is null.</summary>
    public bool IsOpen => StopUtc is null;
}

/// <summary>
/// Start a new journal segment. May auto-close a previous open entry on the same machine
/// when the prior last-seen is recent enough.
/// </summary>
public sealed class StartJournalRequest
{
    /// <summary>Optional client-supplied entry id (offline queue / UUIDv7); server generates if omitted.</summary>
    public Uuid7? Id { get; set; }

    public Uuid7 MachineId { get; set; }

    /// <summary>Existing target id; alternatively supply the customer/project/task triple below.</summary>
    public Uuid7? JournalTargetId { get; set; }

    /// <summary>Customer part of the work triple when not using <see cref="JournalTargetId"/>.</summary>
    public Uuid7? CustomerId { get; set; }

    /// <summary>Project part of the work triple when not using <see cref="JournalTargetId"/>.</summary>
    public Uuid7? ProjectId { get; set; }

    /// <summary>Task part of the work triple when not using <see cref="JournalTargetId"/>.</summary>
    public Uuid7? TaskId { get; set; }

    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Optional start time; defaults to now (UTC).</summary>
    public DateTimeOffset? StartUtc { get; set; }
}

/// <summary>Stop or pause an open journal (sets <c>StopUtc</c>).</summary>
public sealed class StopJournalRequest
{
    /// <summary>Optional stop time; defaults to now (UTC).</summary>
    public DateTimeOffset? StopUtc { get; set; }
}

/// <summary>Partial update for notes, tags, target, or times on an existing entry.</summary>
public sealed class UpdateJournalRequest
{
    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? StopUtc { get; set; }

    /// <summary>Replacement journal target id, or null to leave unchanged.</summary>
    public Uuid7? JournalTargetId { get; set; }

    public string? Notes { get; set; }
    public string? Tags { get; set; }
}
