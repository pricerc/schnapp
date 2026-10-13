namespace Schnapp.Client.Core.Spool;

using System;

/// <summary>
/// Aggregate status of the offline screenshot spool.
/// </summary>
public sealed class SpoolStatus
{
    /// <summary>Spool root directory.</summary>
    public string RootPath { get; set; } = "";

    /// <summary>Number of pending items (including those in backoff).</summary>
    public int PendingCount { get; set; }

    /// <summary>Items with a non-empty last error.</summary>
    public int FailedCount { get; set; }

    /// <summary>Total bytes of image.bin files for pending items.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Oldest enqueue time, if any.</summary>
    public DateTimeOffset? OldestEnqueuedUtc { get; set; }

    /// <summary>Newest enqueue time, if any.</summary>
    public DateTimeOffset? NewestEnqueuedUtc { get; set; }

    /// <summary>Number of already-uploaded items kept locally as a recent-screenshots cache.</summary>
    public int UploadedCount { get; set; }

    /// <summary>Total bytes of image.bin files for already-uploaded (cached) items.</summary>
    public long UploadedBytes { get; set; }
}
