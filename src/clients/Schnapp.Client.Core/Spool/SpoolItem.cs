namespace Schnapp.Client.Core.Spool;

using System;

using Medo;

/// <summary>
/// Whether a local spool item still needs to be uploaded, or is being kept locally
/// as part of the recent-screenshots cache after a successful upload.
/// </summary>
public enum SpoolUploadStatus
{
    /// <summary>Not yet uploaded (may have failed attempts / backoff). Default for legacy rows.</summary>
    Pending = 0,

    /// <summary>Already uploaded to the server; kept locally as a recent-screenshots cache entry.</summary>
    Uploaded = 1
}

/// <summary>
/// Queue-control metadata for one offline screenshot (spool bookkeeping only).
/// Upload payload lives beside this as <c>upload.json</c>; image bytes as <c>image.bin</c>.
/// </summary>
public sealed class SpoolItem
{
    /// <summary>
    /// Unique item id (folder name under the spool root). Sent as
    /// <c>ScreenshotUploadMetadata.Id</c> on push so the server can use a stable client id
    /// (idempotent retries).
    /// </summary>
    public Uuid7 Id { get; set; }

    /// <summary>When the item was enqueued (UTC).</summary>
    public DateTimeOffset EnqueuedUtc { get; set; }

    /// <summary>Original file name for multipart upload.</summary>
    public string FileName { get; set; } = "shot.bin";

    /// <summary>MIME type of the image bytes.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>
    /// Size of <c>image.bin</c> in bytes, cached so status/cap checks don't need to stat the
    /// file. 0 for legacy items written before this field existed (falls back to a file stat).
    /// </summary>
    public long ByteSize { get; set; }

    /// <summary>Number of failed upload attempts.</summary>
    public int Attempts { get; set; }

    /// <summary>Last error message, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>When the last attempt failed (UTC).</summary>
    public DateTimeOffset? LastAttemptUtc { get; set; }

    /// <summary>Earliest time the next push should try this item (UTC), for backoff.</summary>
    public DateTimeOffset? NextAttemptUtc { get; set; }

    /// <summary>Pending (needs upload) or Uploaded (kept as recent-cache entry).</summary>
    public SpoolUploadStatus UploadStatus { get; set; } = SpoolUploadStatus.Pending;

    /// <summary>
    /// Id returned by the server after a successful upload (stored when
    /// <see cref="UploadStatus"/> is Uploaded). Usually equals <see cref="Id"/>; may differ if
    /// the server reassigned the id because the client id conflicted with a non-duplicate row.
    /// </summary>
    public Uuid7? ServerScreenshotId { get; set; }

    /// <summary>When this item was successfully uploaded (UTC), if ever.</summary>
    public DateTimeOffset? UploadedAtUtc { get; set; }

    // --- Upload metadata (mirrors ScreenshotUploadMetadata) -----------------------------------
    // Duplicated here (in addition to the on-disk upload.json) so the unified SQLite database
    // can answer metadata queries without reading/parsing every item folder on disk.

    /// <summary>Machine that captured this screenshot.</summary>
    public Uuid7 MachineId { get; set; }

    /// <summary>Client-reported capture timestamp.</summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>Client's IANA time zone id at capture time.</summary>
    public string? ClientTimeZoneId { get; set; }

    /// <summary>Monitor identifier (for multi-monitor captures).</summary>
    public string? MonitorId { get; set; }

    /// <summary>Human-readable monitor name.</summary>
    public string? MonitorName { get; set; }

    /// <summary>Active window title at capture time.</summary>
    public string? WindowTitle { get; set; }

    /// <summary>Active program/process name at capture time.</summary>
    public string? ProgramName { get; set; }

    /// <summary>Active process command line at capture time.</summary>
    public string? CommandLine { get; set; }

    /// <summary>User-supplied notes.</summary>
    public string? Notes { get; set; }

    /// <summary>User-supplied tags (normalized, comma-separated).</summary>
    public string? Tags { get; set; }

    /// <summary>Journal entry this capture belongs to, if any.</summary>
    public Uuid7? JournalEntryId { get; set; }

    /// <summary>Image width in pixels, if known.</summary>
    public int? Width { get; set; }

    /// <summary>Image height in pixels, if known.</summary>
    public int? Height { get; set; }
}
