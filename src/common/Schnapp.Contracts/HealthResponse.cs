namespace Schnapp.Contracts;

/// <summary>
/// Payload for <c>GET /api/v1/health</c> (liveness/readiness style check).
/// </summary>
public sealed class HealthResponse
{
    /// <summary><c>ok</c> when the process can serve; <c>degraded</c> when the DB check fails.</summary>
    public string Status { get; set; } = "ok";

    /// <summary>Database reachability detail (e.g. <c>up</c> or error type name).</summary>
    public string? Database { get; set; }
}
