namespace Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Org-level customer (client) in the shared list hierarchy used by journal targets.
/// </summary>
public sealed class Customer
{
    public Uuid7 Id { get; set; }

    /// <summary>Short unique code (often auto-generated from the name).</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>Free-text tags (space- or comma-separated in UI conventions).</summary>
    public string? Tags { get; set; }

    /// <summary>List status; see <see cref="Schnapp.Domain.ListEntityStatus"/>.</summary>
    public int Status { get; set; }

    /// <summary>Optional CSS/hex colour for UI badges.</summary>
    public string? ColourCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
