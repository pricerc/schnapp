namespace Schnapp.Domain.Entities;

/// <summary>
/// Lookup row describing a numeric status value for a named entity type (e.g. machine, user).
/// </summary>
public sealed class EntityStatus
{
    /// <summary>Entity type key (e.g. <c>machine</c>, <c>customer</c>).</summary>
    public string Entity { get; set; } = "";

    /// <summary>Numeric status code stored on entity rows.</summary>
    public int Status { get; set; }

    public string Description { get; set; } = "";

    /// <summary>Optional CSS/hex colour for UI badges.</summary>
    public string? ColourCode { get; set; }
}
