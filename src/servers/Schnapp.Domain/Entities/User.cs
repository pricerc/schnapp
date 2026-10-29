namespace Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Application user account (login identity, role, and profile fields).
/// </summary>
public sealed class User
{
    public Uuid7 Id { get; set; }

    /// <summary>Short unique code usable as a login identity.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Unique email; preferred login identity.</summary>
    public string Email { get; set; } = "";

    /// <summary>Password hash; empty string means passwordless login is allowed.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>Role string; see <see cref="Schnapp.Domain.UserRole"/>.</summary>
    public string Role { get; set; } = UserRole.User;

    /// <summary>IANA timezone used for day-browser and local presentation.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    public string? Tags { get; set; }
    public string? Notes { get; set; }

    /// <summary>List status; see <see cref="Schnapp.Domain.ListEntityStatus"/>.</summary>
    public int Status { get; set; }

    /// <summary>Optional CSS/hex colour for UI badges.</summary>
    public string? ColourCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Last successful login, if any.</summary>
    public DateTimeOffset? LastLogin { get; set; }

    /// <summary>Last observed API activity, if tracked.</summary>
    public DateTimeOffset? LastSeen { get; set; }
}
