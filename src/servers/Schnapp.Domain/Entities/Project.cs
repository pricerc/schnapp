namespace Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Project under a customer in the shared list hierarchy used by journal targets.
/// </summary>
public sealed class Project
{
    public Uuid7 Id { get; set; }

    /// <summary>Short unique code (often auto-generated from the name).</summary>
    public string Code { get; set; } = "";

    /// <summary>Parent customer; may be the seeded NONE sentinel.</summary>
    public Uuid7 CustomerId { get; set; }

    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>List status; see <see cref="Schnapp.Domain.ListEntityStatus"/>.</summary>
    public int Status { get; set; }

    /// <summary>Optional CSS/hex colour for UI badges.</summary>
    public string? ColourCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
