namespace Schnapp.Contracts;

using Medo;

/// <summary>Metadata returned for a screenshot (no image bytes).</summary>
public sealed class ScreenshotResponse
{
    public Uuid7 Id { get; set; }
    public Uuid7 UserId { get; set; }
    public Uuid7 MachineId { get; set; }

    /// <summary>Client-reported capture time (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>IANA timezone id from the capturing client, if provided.</summary>
    public string? ClientTimeZoneId { get; set; }

    /// <summary>When the API accepted the upload (UTC).</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    public string? MonitorId { get; set; }
    public string? MonitorName { get; set; }
    public string? WindowTitle { get; set; }
    public string? ProgramName { get; set; }

    /// <summary>MIME type of the stored image.</summary>
    public string ContentType { get; set; } = "";

    public long ByteSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Client-computed visual fingerprint when provided at upload.</summary>
    public ScreenshotFingerprint? Fingerprint { get; set; }

    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Journal entry open when the shot was taken, if any.</summary>
    public Uuid7? JournalEntryId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the screenshot was soft-deleted. Null while it is still in the gallery.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Request body to delete multiple screenshots owned by the caller in one call.</summary>
public sealed class ScreenshotBulkDeleteRequest
{
    /// <summary>Screenshot ids to delete.</summary>
    public List<Uuid7> Ids { get; set; } = new();
}

/// <summary>Result of a bulk delete: how many of the requested ids were actually deleted.</summary>
public sealed class ScreenshotBulkDeleteResponse
{
    /// <summary>Count of rows removed (owned by the caller and present).</summary>
    public int Deleted { get; set; }
}

/// <summary>How many owned rows a bulk soft-delete, restore, or purge changed.</summary>
public sealed class ScreenshotBulkMutationResponse
{
    public int Count { get; set; }
}

/// <summary>JSON metadata part of multipart screenshot upload.</summary>
/// <remarks>
/// Optional <see cref="Id"/> is the client-chosen screenshot id (typically the spool item id).
/// On conflict the server may accept it as an idempotent retry or assign a different id.
/// </remarks>
public sealed class ScreenshotUploadMetadata
{
    /// <summary>
    /// Client-chosen screenshot id for offline spool / idempotent retry; server generates if omitted.
    /// </summary>
    public Uuid7? Id { get; set; }

    public Uuid7 MachineId { get; set; }

    /// <summary>Client-reported capture time; defaults to server receive time when omitted.</summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>IANA timezone id from the capturing client.</summary>
    public string? ClientTimeZoneId { get; set; }

    public string? MonitorId { get; set; }
    public string? MonitorName { get; set; }
    public string? WindowTitle { get; set; }
    public string? ProgramName { get; set; }
    public string? CommandLine { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Journal entry that was open at capture time, if any.</summary>
    public Uuid7? JournalEntryId { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>
    /// Client-computed visual fingerprint (source-pixel grid + hashes). Used for idle/blank
    /// skip on the client and housekeeping/reports on the server.
    /// </summary>
    public ScreenshotFingerprint? Fingerprint { get; set; }
}

/// <summary>Versioned visual fingerprint stored with a screenshot.</summary>
public sealed class ScreenshotFingerprint
{
    /// <summary>Algorithm version (<c>1</c> = 240x135-capped grid + a/d/pHash).</summary>
    public int Version { get; set; } = 1;

    public int GridCols { get; set; }
    public int GridRows { get; set; }

    /// <summary>Per-cell mean luma (length = cols x rows). JSON is base64.</summary>
    public byte[]? CellMeans { get; set; }

    public double MeanLuma { get; set; }
    public int MaxLuma { get; set; }

    /// <summary>64-bit aHash as lowercase hex.</summary>
    public string? AHash { get; set; }

    /// <summary>64-bit dHash as lowercase hex.</summary>
    public string? DHash { get; set; }

    /// <summary>64-bit pHash as lowercase hex.</summary>
    public string? PHash { get; set; }
}

/// <summary>Partial update for notes/tags/journal link.</summary>
/// <remarks>
/// <see cref="Notes"/> / <see cref="Tags"/>: <c>null</c> leaves the field unchanged;
/// empty string clears it. Always send a string when the UI intends a full replace.
/// </remarks>
public sealed class UpdateScreenshotRequest
{
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    /// <summary>
    /// When <see cref="SetJournalEntryId"/> is true, replaces the linked journal
    /// (null clears the link). When false, the existing link is left unchanged.
    /// </summary>
    public Uuid7? JournalEntryId { get; set; }
    /// <summary>If true, apply <see cref="JournalEntryId"/> (including null to clear).</summary>
    public bool SetJournalEntryId { get; set; }
}

/// <summary>Find idle/blank duplicates among screenshots the caller owns.</summary>
public sealed class ScreenshotDuplicateRequest
{
    /// <summary>Screenshot ids to consider (typically the visible day gallery).</summary>
    public List<Uuid7> Ids { get; set; } = new();

    /// <summary>Max dirty grid cells still counted as idle (1-20). Default 2.</summary>
    public int MaxDirtyCells { get; set; } = 2;
}

/// <summary>Ids that would be discarded as blank or idle duplicates.</summary>
public sealed class ScreenshotDuplicateResponse
{
    public List<Uuid7> DuplicateIds { get; set; } = new();
    public int Backfilled { get; set; }
    public int FingerprintFailed { get; set; }
}

/// <summary>Fill missing visual fingerprints for the caller's screenshots.</summary>
public sealed class ScreenshotFingerprintBackfillRequest
{
    /// <summary>Local calendar date <c>yyyy-MM-dd</c> in the user's timezone.</summary>
    public string? Date { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Limit { get; set; } = 100;
}

/// <summary>How many fingerprints were written in one backfill pass.</summary>
public sealed class ScreenshotFingerprintBackfillResponse
{
    public int Processed { get; set; }
    public int Failed { get; set; }
    public int Remaining { get; set; }
}
