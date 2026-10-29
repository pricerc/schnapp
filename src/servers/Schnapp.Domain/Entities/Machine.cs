namespace Schnapp.Domain.Entities;

using Medo;

/// <summary>
/// Physical or virtual workstation that captures screenshots and hosts journal activity.
/// </summary>
public sealed class Machine
{
    public Uuid7 Id { get; set; }

    /// <summary>Short unique code.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Fully-qualified DNS name reported by the client, if known.</summary>
    public string? Fqdn { get; set; }

    /// <summary>Stable hardware identity from the client OS for re-recognition.</summary>
    public string? HardwareFingerprint { get; set; }

    /// <summary>JSON array of recent IP address strings, if tracked.</summary>
    public string? LastIpAddresses { get; set; }

    /// <summary>Optional machine-level password hash (rarely used; prefer user tokens).</summary>
    public string? OptionalPasswordHash { get; set; }

    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Machine status; see <see cref="Schnapp.Domain.MachineStatus"/>.</summary>
    public int Status { get; set; }

    /// <summary>Optional CSS/hex colour for UI badges.</summary>
    public string? ColourCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Last observed activity from a client on this machine.</summary>
    public DateTimeOffset? LastSeen { get; set; }
}
