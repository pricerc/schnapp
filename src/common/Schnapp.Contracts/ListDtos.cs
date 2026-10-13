namespace Schnapp.Contracts;

using Medo;

/// <summary>Shared list item fields for customer, project, and task API responses.</summary>
public sealed class ListItemResponse
{
    public Uuid7 Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Parent customer for projects and tasks; null on customers.</summary>
    public Uuid7? CustomerId { get; set; }

    /// <summary>Parent project for tasks; null on customers and projects.</summary>
    public Uuid7? ProjectId { get; set; }

    /// <summary>List status code (0 = Active, 1 = Inactive).</summary>
    public int Status { get; set; }

    /// <summary>Optional CSS/hex colour for UI badges.</summary>
    public string? ColourCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Create or update body for a customer, project, or task list entity.</summary>
public sealed class ListItemRequest
{
    /// <summary>Optional client-supplied id (UUIDv7); server generates if omitted.</summary>
    public Uuid7? Id { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Required for project/task; defaults to NONE when omitted.</summary>
    public Uuid7? CustomerId { get; set; }

    /// <summary>Required for task; defaults to NONE when omitted.</summary>
    public Uuid7? ProjectId { get; set; }

    /// <summary>List status code (0 = Active, 1 = Inactive).</summary>
    public int Status { get; set; }

    /// <summary>Optional CSS/hex colour for UI badges.</summary>
    public string? ColourCode { get; set; }
}

/// <summary>Status lookup row for a named entity type.</summary>
public sealed class EntityStatusResponse
{
    /// <summary>Entity type key (e.g. <c>machine</c>, <c>customer</c>).</summary>
    public string Entity { get; set; } = "";

    public int Status { get; set; }
    public string Description { get; set; } = "";

    /// <summary>Optional CSS/hex colour for UI badges.</summary>
    public string? ColourCode { get; set; }
}

/// <summary>Deduplicated customer/project/task triple used as a journal work target.</summary>
public sealed class JournalTargetResponse
{
    public Uuid7 Id { get; set; }
    public Uuid7 CustomerId { get; set; }
    public Uuid7 ProjectId { get; set; }
    public Uuid7 TaskId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string? Tags { get; set; }
}

/// <summary>Get-or-create a journal target by customer/project/task triple.</summary>
public sealed class JournalTargetRequest
{
    /// <summary>Optional client-supplied id; server may generate or reuse an existing triple.</summary>
    public Uuid7? Id { get; set; }

    public Uuid7 CustomerId { get; set; }
    public Uuid7 ProjectId { get; set; }
    public Uuid7 TaskId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
}

/// <summary>Partial update for an existing journal target's display fields.</summary>
public sealed class UpdateJournalTargetRequest
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
}

/// <summary>Tag dictionary entry for autocomplete / admin.</summary>
public sealed class TagResponse
{
    public string Tag { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>Customer scope; null means org-global.</summary>
    public Uuid7? CustomerId { get; set; }
}

/// <summary>Create tag request (also used for explicit dictionary inserts).</summary>
public sealed class TagRequest
{
    public string Tag { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>Optional customer scope; null/omitted = global.</summary>
    public Uuid7? CustomerId { get; set; }
}

/// <summary>Update notes on an existing dictionary tag (identity: Tag + CustomerId).</summary>
public sealed class UpdateTagRequest
{
    /// <summary>Canonical tag text (must match existing row).</summary>
    public string Tag { get; set; } = "";

    /// <summary>Current scope; null = global row.</summary>
    public Uuid7? CustomerId { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// Move a global tag to a customer (assign), or a customer tag to global (generalize).
/// </summary>
public sealed class ReassignTagRequest
{
    public string Tag { get; set; } = "";

    /// <summary>
    /// When assigning global → customer: target customer id.
    /// When generalizing customer → global: source customer id.
    /// </summary>
    public Uuid7 CustomerId { get; set; }
}

/// <summary>Org setting key/value pair.</summary>
public sealed class SettingResponse
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Admin patch for a setting value.</summary>
public sealed class UpdateSettingRequest
{
    public string Value { get; set; } = "";
    public string? Notes { get; set; }
}
