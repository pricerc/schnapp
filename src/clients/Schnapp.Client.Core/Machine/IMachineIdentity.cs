namespace Schnapp.Client.Core.Machine;

/// <summary>
/// Provides host identity signals for machine register/find.
/// </summary>
/// <remarks>
/// Platform-specific implementations may use machine-id, MAC, or hostname.
/// Core ships <see cref="DefaultMachineIdentity"/> with portable FQDN/hostname.
/// </remarks>
public interface IMachineIdentity
{
    /// <summary>DNS host name or FQDN when available.</summary>
    string GetFqdn();

    /// <summary>Stable-ish hardware fingerprint (may be empty if unknown).</summary>
    string GetHardwareFingerprint();

    /// <summary>Suggested display name for a new machine registration.</summary>
    string GetSuggestedName();
}
