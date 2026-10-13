namespace Schnapp.Client.Core.Machine;

using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;

/// <summary>
/// Portable machine identity: hostname/FQDN plus best-effort fingerprint (machine-id or MAC).
/// </summary>
public sealed class DefaultMachineIdentity : IMachineIdentity
{
    /// <inheritdoc />
    public string GetFqdn()
    {
        try
        {
            var host = Dns.GetHostName();
            try
            {
                var entry = Dns.GetHostEntry(host);
                if (!string.IsNullOrWhiteSpace(entry.HostName))
                {

                    return entry.HostName;
                }

            }
            catch
            {
                // ignore
            }
            return host;
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    /// <inheritdoc />
    public string GetHardwareFingerprint()
    {
        // Linux /etc/machine-id
        try
        {
            const string machineIdPath = "/etc/machine-id";
            if (File.Exists(machineIdPath))
            {
                var id = File.ReadAllText(machineIdPath).Trim();
                if (id.Length > 0)
                {

                    return "machine-id:" + id;
                }

            }
        }
        catch
        {
            // ignore
        }

        // First non-loopback MAC
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var adr = nic.GetPhysicalAddress();
                if (adr == null || adr.GetAddressBytes().Length == 0)
                {
                    continue;
                }

                var mac = adr.ToString();
                if (string.IsNullOrEmpty(mac?.TrimStart('0')))
                {
                    continue;
                }
#pragma warning disable CS8602 // null dereference: we check for null/empty
                return "mac:" + mac.ToLowerInvariant();
#pragma warning restore CS8602 // null dereference: we check for null/empty

            }
        }
        catch
        {
            // ignore
        }

        return "host:" + Environment.MachineName;
    }

    /// <inheritdoc />
    public string GetSuggestedName() => Environment.MachineName;
}
