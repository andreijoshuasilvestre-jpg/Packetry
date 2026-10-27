using UnityEngine;

/// <summary>
/// Cross-device IP-address-in-use check — a PC or a Router interface taking an
/// address another device on the network already holds is a real address
/// conflict (same as a duplicate static IP breaking a real network), not
/// something SubnetMath.SameSubnet/ReachabilityService models on its own since
/// they only care about subnet membership, not per-host uniqueness. Scans
/// every PC's DeviceAddress and every Router's configured interfaces.
/// </summary>
public static class AddressConflict
{
    /// <summary>Human-readable description of whatever is already using ip, or
    /// null if it's free. excludeDevice/excludePort identify the one
    /// device+port currently being edited, so re-applying its own current
    /// address doesn't flag itself as a conflict.</summary>
    public static string Find(string ip, NetworkDevice excludeDevice, int excludePort = -1)
    {
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.deviceType == DeviceType.PC)
            {
                if (device == excludeDevice) continue;

                var addr = device.GetComponent<DeviceAddress>();
                if (addr != null && addr.HasAddress && addr.Ip == ip)
                    return $"the PC \"{device.deviceName}\"";
            }
            else if (device.deviceType == DeviceType.Router)
            {
                if (CLIPanelManager.Instance == null) continue;

                RouterCLI cli = CLIPanelManager.Instance.GetOrCreateSession(device);
                for (int p = 0; p < device.PortCount; p++)
                {
                    if (device == excludeDevice && p == excludePort) continue;
                    if (!device.IsPortActive(p)) continue;
                    if (cli.GetInterfaceIp(p) == ip)
                        return $"the Router's eth{p} interface";
                }
            }
        }
        return null;
    }
}
