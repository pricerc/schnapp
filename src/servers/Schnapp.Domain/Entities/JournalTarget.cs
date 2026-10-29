namespace Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Deduplicated customer/project/task triple used as the work target of journal entries.
/// Uniqueness is on the triple; code and name are display fields.
/// </summary>
public sealed class JournalTarget
{
    public Uuid7 Id { get; set; }
    public Uuid7 CustomerId { get; set; }
    public Uuid7 ProjectId { get; set; }
    public Uuid7 TaskId { get; set; }

    /// <summary>Optional short code for display (may be empty).</summary>
    public string Code { get; set; } = "";

    /// <summary>Optional display name (may be empty; UI often composes from list names).</summary>
    public string Name { get; set; } = "";

    public string? Notes { get; set; }
    public string? Tags { get; set; }
}
