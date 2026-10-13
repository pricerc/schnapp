namespace Schnapp.Client.Core.Upload;

using System;
using Schnapp.Contracts;

using Medo;

/// <summary>Payload for <see cref="MutationKind.ListCreate"/>.</summary>
public sealed class ListCreatePayload
{
    public string Kind { get; set; } = "customers"; // customers|projects|tasks
    public ListItemRequest Item { get; set; } = new ListItemRequest();
}

/// <summary>Payload for <see cref="MutationKind.JournalStart"/>.</summary>
public sealed class JournalStartPayload
{
    public StartJournalRequest Request { get; set; } = new StartJournalRequest();
}

/// <summary>Payload for <see cref="MutationKind.JournalStop"/>.</summary>
public sealed class JournalStopPayload
{
    public Uuid7 JournalEntryId { get; set; }
    public DateTimeOffset? StopUtc { get; set; }
}

/// <summary>Payload for <see cref="MutationKind.JournalUpdate"/>.</summary>
public sealed class JournalUpdatePayload
{
    public Uuid7 JournalEntryId { get; set; }
    public UpdateJournalRequest Request { get; set; } = new UpdateJournalRequest();
}

/// <summary>Payload for <see cref="MutationKind.JournalTargetEnsure"/>.</summary>
public sealed class JournalTargetEnsurePayload
{
    public JournalTargetRequest Request { get; set; } = new JournalTargetRequest();
}

/// <summary>Payload for <see cref="MutationKind.ScreenshotUpdate"/>.</summary>
public sealed class ScreenshotUpdatePayload
{
    /// <summary>Server screenshot id (usually the spool id or <c>ServerScreenshotId</c>).</summary>
    public Uuid7 ScreenshotId { get; set; }

    /// <summary>Partial update body (notes, tags, journal link).</summary>
    public UpdateScreenshotRequest Request { get; set; } = new UpdateScreenshotRequest();
}
