using Medo;

namespace Schnapp.Domain.Entities;

/// <summary>
/// Metadata for a captured screen image stored as blobs under <see cref="StorageKey"/>
/// and <see cref="ThumbStorageKey"/>.
/// </summary>
public sealed class Screenshot
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

    /// <summary>Client monitor identifier when capturing a specific display.</summary>
    public string? MonitorId { get; set; }

    public string? MonitorName { get; set; }
    public string? WindowTitle { get; set; }
    public string? ProgramName { get; set; }
    public string? CommandLine { get; set; }

    /// <summary>MIME type of the stored image (e.g. image/jpeg).</summary>
    public string ContentType { get; set; } = "";

    public long ByteSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Hex SHA-256 of the image bytes, if computed.</summary>
    public string? Sha256 { get; set; }

    /// <summary>
    /// JSON of the client visual fingerprint (jsonb). Null on rows captured before this column.
    /// </summary>
    public string? Fingerprint { get; set; }

    /// <summary>Relative key of the full image in the blob store.</summary>
    public string StorageKey { get; set; } = "";

    /// <summary>Relative key of the generated gallery thumbnail.</summary>
    public string ThumbStorageKey { get; set; } = "";

    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Journal entry that was open when the shot was taken, if any.</summary>
    public Uuid7? JournalEntryId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
