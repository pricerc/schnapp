namespace Schnapp.Client.Windows.Machine;

using Microsoft.Win32;

using Schnapp.Client.Core.Machine;

/// <summary>
/// Windows machine identity: prefers MachineGuid, then portable fallbacks from <see cref="DefaultMachineIdentity"/>.
/// </summary>
/// <remarks>
/// Used by <c>MachinesForm</c> for register/find/link. Keeps Registry access out of Client.Core.
/// </remarks>
public sealed class WindowsMachineIdentity : IMachineIdentity
{
    private readonly DefaultMachineIdentity _fallback = new();

    /// <inheritdoc />
    public string GetFqdn() => _fallback.GetFqdn();

    /// <inheritdoc />
    public string GetSuggestedName() => _fallback.GetSuggestedName();

    /// <inheritdoc />
    public string GetHardwareFingerprint()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                var guid = key?.GetValue("MachineGuid") as string;
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    return "machine-guid:" + guid!.Trim();
                }
            }
        }
        catch
        {
            // ignore
        }

        return _fallback.GetHardwareFingerprint();
    }
}
