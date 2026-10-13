namespace Schnapp.Client.Core.Upload;

using System;

using Medo;

/// <summary>Kinds of offline mutations that can be flushed when the API is back.</summary>
public enum MutationKind
{
    /// <summary>POST /lists/{customers|projects|tasks}</summary>
    ListCreate = 1,

    /// <summary>POST /journals/start</summary>
    JournalStart = 2,

    /// <summary>POST /journals/{id}/stop</summary>
    JournalStop = 3,

    /// <summary>PATCH /journals/{id}</summary>
    JournalUpdate = 4,

    /// <summary>POST /lists/journal-targets (get-or-create)</summary>
    JournalTargetEnsure = 5,

    /// <summary>PATCH /screenshots/{id} (notes/tags/journal link)</summary>
    ScreenshotUpdate = 6
}

/// <summary>
/// One durable offline mutation stored in the client SQLite <c>mutation</c> table
/// (payload as JSON text).
/// </summary>
public sealed class MutationItem
{
    /// <summary>Unique mutation id (primary key in the client <c>mutation</c> table).</summary>
    public Uuid7 Id { get; set; }

    /// <summary>Which API mutation this row represents.</summary>
    public MutationKind Kind { get; set; }

    /// <summary>When the mutation was enqueued (UTC).</summary>
    public DateTimeOffset EnqueuedUtc { get; set; }

    /// <summary>JSON payload for the kind (see payload record types in this namespace).</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>Number of failed push attempts.</summary>
    public int Attempts { get; set; }

    /// <summary>Last error message, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>When the last attempt failed (UTC).</summary>
    public DateTimeOffset? LastAttemptUtc { get; set; }

    /// <summary>Earliest time the next push should retry (UTC), for exponential backoff.</summary>
    public DateTimeOffset? NextAttemptUtc { get; set; }

    /// <summary>
    /// True once a push attempt failed with a permanent (non-retryable) API error — e.g. a
    /// 400/404/409/422 business-logic rejection such as a foreign key that will never resolve
    /// on retry. Dead items are skipped by <c>UploadService.PushAsync</c> (no more retries /
    /// backoff) and excluded from <c>PendingCount</c>, but stay in the DB until explicitly cleared
    /// via <c>UploadService.ClearDeadItems</c> so they can be inspected.
    /// </summary>
    public bool Dead { get; set; }
}
