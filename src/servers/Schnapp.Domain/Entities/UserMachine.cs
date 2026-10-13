using Medo;

namespace Schnapp.Domain.Entities;

/// <summary>
/// Association between a user and a machine (registration, per-user display name, client prefs).
/// Composite key is <see cref="UserId"/> + <see cref="MachineId"/>.
/// </summary>
public sealed class UserMachine
{
    public Uuid7 UserId { get; set; }
    public Uuid7 MachineId { get; set; }

    /// <summary>When the user first registered or linked this machine.</summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>Per-user display name override for this machine link.</summary>
    public string? DisplayName { get; set; }

    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Status of the user-machine link.</summary>
    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Last activity observed on this link.</summary>
    public DateTimeOffset? LastSeen { get; set; }

    /// <summary>Last metadata update on this link.</summary>
    public DateTimeOffset? LastUpdated { get; set; }

    /// <summary>JSON blob of client-side preferences synced for this user/machine pair.</summary>
    public string? ClientSettings { get; set; }
}
