namespace Schnapp.Contracts;

using Medo;

/// <summary>Request to register a new machine for the current user.</summary>
public sealed class CreateMachineRequest
{
    /// <summary>Display name (required).</summary>
    public string Name { get; set; } = "";

    /// <summary>Short unique code; derived from name if omitted.</summary>
    public string? Code { get; set; }

    /// <summary>Fully-qualified DNS name, if known.</summary>
    public string? Fqdn { get; set; }

    /// <summary>Stable hardware fingerprint from the client OS.</summary>
    public string? HardwareFingerprint { get; set; }

    /// <summary>Optional notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Optional per-user display name for this machine link.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Machine status code (default 0 = Active).</summary>
    public int Status { get; set; }

    /// <summary>Optional display colour.</summary>
    public string? ColourCode { get; set; }
}

/// <summary>Request to find machines matching this PC (fingerprint / FQDN).</summary>
public sealed class FindMachinesRequest
{
    /// <summary>Current machine name hint.</summary>
    public string? Name { get; set; }

    /// <summary>FQDN reported by the client.</summary>
    public string? Fqdn { get; set; }

    /// <summary>Hardware fingerprint reported by the client.</summary>
    public string? HardwareFingerprint { get; set; }
}

/// <summary>One candidate returned by machine find matching.</summary>
public sealed class MachineFindCandidate
{
    /// <summary>Existing machine id.</summary>
    public Uuid7 Id { get; set; }

    /// <summary>Machine name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Machine code.</summary>
    public string Code { get; set; } = "";

    /// <summary>Stored FQDN, if any.</summary>
    public string? Fqdn { get; set; }

    /// <summary>Stored fingerprint, if any.</summary>
    public string? HardwareFingerprint { get; set; }

    /// <summary>Match strength: high, medium, or low.</summary>
    public string Confidence { get; set; } = "low";

    /// <summary>Why this row matched.</summary>
    public string Reason { get; set; } = "";
}

/// <summary>Response listing find candidates.</summary>
public sealed class FindMachinesResponse
{
    /// <summary>Ordered candidates (best first).</summary>
    public List<MachineFindCandidate> Candidates { get; set; } = new();
}

/// <summary>Public machine view for the current user.</summary>
public sealed class MachineResponse
{
    /// <summary>Machine id.</summary>
    public Uuid7 Id { get; set; }

    /// <summary>Short code.</summary>
    public string Code { get; set; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>FQDN if known.</summary>
    public string? Fqdn { get; set; }

    /// <summary>Hardware fingerprint if known.</summary>
    public string? HardwareFingerprint { get; set; }

    /// <summary>JSON array of recent IPs (string), if any.</summary>
    public string? LastIpAddresses { get; set; }

    /// <summary>When the user registered this machine.</summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>Per-user display name override.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Last activity on the machine row.</summary>
    public DateTimeOffset? LastSeen { get; set; }

    /// <summary>Machine status code.</summary>
    public int Status { get; set; }

    /// <summary>Optional display colour.</summary>
    public string? ColourCode { get; set; }

    /// <summary>When the machine row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the machine row was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Status on the user-machine link.</summary>
    public int LinkStatus { get; set; }

    /// <summary>Machine notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Machine tags.</summary>
    public string? Tags { get; set; }
}

/// <summary>Update machine identity and metadata (owner or admin).</summary>
public sealed class UpdateMachineRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Fqdn { get; set; }
    public string? HardwareFingerprint { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }

    /// <summary>Machine status code (0 = Active, 1 = Disabled).</summary>
    public int Status { get; set; }

    public string? ColourCode { get; set; }

    /// <summary>Per-user display name on the link (owner only; ignored for admin org edit when no link).</summary>
    public string? DisplayName { get; set; }
}

/// <summary>Admin bulk status change for machines.</summary>
public sealed class BulkMachineStatusRequest
{
    /// <summary>Machine ids to update.</summary>
    public List<Uuid7> Ids { get; set; } = new();

    /// <summary>New machine status code applied to each id.</summary>
    public int Status { get; set; }
}

/// <summary>Result of bulk machine status update.</summary>
public sealed class BulkMachineStatusResponse
{
    /// <summary>Number of machine rows whose status was changed.</summary>
    public int Updated { get; set; }
}

/// <summary>Admin (org-wide) machine view, including the owning user's identity.</summary>
public sealed class AdminMachineResponse
{
    /// <summary>Machine id.</summary>
    public Uuid7 Id { get; set; }

    /// <summary>Short code.</summary>
    public string Code { get; set; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>FQDN if known.</summary>
    public string? Fqdn { get; set; }

    /// <summary>Hardware fingerprint if known.</summary>
    public string? HardwareFingerprint { get; set; }

    /// <summary>Last activity on the machine row.</summary>
    public DateTimeOffset? LastSeen { get; set; }

    /// <summary>Machine status code.</summary>
    public int Status { get; set; }

    /// <summary>Optional display colour.</summary>
    public string? ColourCode { get; set; }

    /// <summary>When the machine row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the machine row was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Owning user's id.</summary>
    public Uuid7 UserId { get; set; }

    /// <summary>Owning user's display name.</summary>
    public string UserName { get; set; } = "";

    /// <summary>Owning user's email.</summary>
    public string UserEmail { get; set; } = "";

    /// <summary>Machine notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Machine tags.</summary>
    public string? Tags { get; set; }
}

