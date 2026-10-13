using Medo;

namespace Schnapp.Domain.Entities;

/// <summary>
/// Persisted API bearer token for a user. Only the hash is stored; plaintext is shown once at creation.
/// </summary>
public sealed class ApiToken
{
    public Uuid7 Id { get; set; }
    public Uuid7 UserId { get; set; }

    /// <summary>SHA-256 hex digest of the plaintext bearer secret.</summary>
    public string TokenHash { get; set; } = "";

    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last successful authentication with this token, if any.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>When set, the token is rejected by auth middleware.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public string? Notes { get; set; }
}
