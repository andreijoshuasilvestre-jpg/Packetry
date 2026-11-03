using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The common lookups a level's HintGuide points its markers at — "the first
/// PC with no cable", "the button that gets you to the Cable tool right now".
/// HintGuideManager re-runs a guide's lookups while it's up, so each one is
/// written against the live field and returns null (or a null pair half)
/// when there's nothing to point at.
/// </summary>
public static class HintTargets
{
    // ── Devices ──────────────────────────────────────────────────────────────

    /// <summary>The first device with nothing plugged into any of its ports.</summary>
    public static NetworkDevice FirstUncabled(IReadOnlyList<NetworkDevice> devices)
    {
        foreach (var d in devices)
            if (d != null && !IsCabled(d)) return d;
        return null;
    }

    /// <summary>The first device with no cable running straight to `to`.</summary>
    public static NetworkDevice FirstNotCabledTo(IReadOnlyList<NetworkDevice> devices, NetworkDevice to)
    {
        if (to == null) return null;
        foreach (var d in devices)
            if (d != null && !IsCabledTo(d, to)) return d;
        return null;
    }

    /// <summary>The next cable a "PCs into their Switch, Switches into the
    /// Router" task is missing: a PC with no cable at all, else a Switch with no
    /// cable to the Router (paired with that Router).</summary>
    public static (NetworkDevice a, NetworkDevice b) NextDepartmentCable(
        IReadOnlyList<NetworkDevice> pcs, IReadOnlyList<NetworkDevice> switches, NetworkDevice router)
    {
        var pc = FirstUncabled(pcs);
        if (pc != null) return (pc, null);
        var sw = FirstNotCabledTo(switches, router);
        return sw != null ? (sw, router) : (null, null);
    }

    /// <summary>Where `pc`'s department plugs in: the device another PC on its
    /// subnet is cabled to — for a PC that's addressed but unplugged.</summary>
    public static NetworkDevice SubnetMateUplink(NetworkDevice pc, IReadOnlyList<NetworkDevice> pcs)
    {
        var a = AddressOf(pc);
        if (a == null) return null;
        foreach (var other in pcs)
        {
            var b = other != pc ? AddressOf(other) : null;
            if (b == null || !SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) continue;
            for (int i = 0; i < other.PortCount; i++)
            {
                var cable = other.GetPortCable(i);
                if (cable != null) return cable.DeviceA == other ? cable.DeviceB : cable.DeviceA;
            }
        }
        return null;
    }

    /// <summary>The first PC with no IP address yet.</summary>
    public static NetworkDevice FirstUnaddressed(IReadOnlyList<NetworkDevice> pcs)
    {
        foreach (var pc in pcs)
            if (pc != null && AddressOf(pc) == null) return pc;
        return null;
    }

    /// <summary>The PC whose address most likely needs fixing, in a level that
    /// groups its PCs into departments: one with no address yet; else, once
    /// `router` has an address on any port, one on a subnet none of its ports
    /// is on (departments are set up Router first, PCs to match); else one
    /// that shares its subnet with no other PC. `router` may be null.</summary>
    public static NetworkDevice FirstMisaddressed(IReadOnlyList<NetworkDevice> pcs, NetworkDevice router = null)
    {
        var unaddressed = FirstUnaddressed(pcs);
        if (unaddressed != null) return unaddressed;

        bool routerSet = HasAddressedPort(router);
        foreach (var pc in pcs)
        {
            var a = AddressOf(pc);
            if (a == null) continue;
            if (routerSet ? !RouterOnSubnet(router, a) : SubnetMate(pc, a, pcs) == null) return pc;
        }
        return null;
    }

    /// <summary>Two PCs on the same subnet to ping between, led by the most
    /// recently (re)addressed one — so right after fixing an address, the guide
    /// points at that fix and its neighbor.</summary>
    public static (NetworkDevice a, NetworkDevice b) SameSubnetPair(IReadOnlyList<NetworkDevice> pcs)
    {
        NetworkDevice bestA = null, bestB = null;
        long bestTicks = long.MinValue;
        foreach (var pc in pcs)
        {
            var a = AddressOf(pc);
            if (a == null || a.AddressedAtTicks <= bestTicks) continue;
            var mate = SubnetMate(pc, a, pcs);
            if (mate == null) continue;
            bestA = pc;
            bestB = mate;
            bestTicks = a.AddressedAtTicks;
        }
        return (bestA, bestB);
    }

    /// <summary>The first pair of PCs that can't ping each other.</summary>
    public static (NetworkDevice a, NetworkDevice b) FirstUnreachablePair(IReadOnlyList<NetworkDevice> pcs)
    {
        for (int i = 0; i < pcs.Count; i++)
            for (int j = i + 1; j < pcs.Count; j++)
                if (pcs[i] != null && pcs[j] != null && !ReachabilityService.Ping(pcs[i], pcs[j]).Success)
                    return (pcs[i], pcs[j]);
        return (null, null);
    }

    /// <summary>Both ends of the cable the network can't afford to lose (see
    /// ReachabilityService.FindCriticalCable).</summary>
    public static (NetworkDevice a, NetworkDevice b) CriticalCableEnds(IReadOnlyList<NetworkDevice> pcs, IReadOnlyList<Cable> cables)
    {
        var cut = ReachabilityService.FindCriticalCable(pcs, cables);
        return cut != null ? (cut.DeviceA, cut.DeviceB) : (null, null);
    }

    // ── Buttons ──────────────────────────────────────────────────────────────

    /// <summary>The sidebar control that gets the player to `tool` right now:
    /// the tool's own button while it's the devices button's face or the flyout
    /// is open, otherwise the arrow that opens the flyout.</summary>
    public static RectTransform DeviceTool(CompoundToolKind tool)
    {
        var ui = NetworkUIManager.Instance;
        if (ui == null) return null;
        return ui.DevicesFace == tool || ui.IsDevicesFlyoutOpen ? ui.GetCompoundToolRect(tool) : ui.DevicesFlyoutArrowRect;
    }

    /// <summary>DeviceTool for the first kind a "place these" task is still
    /// short of — PCs, then Switches, then a Router — or null once it's short of
    /// nothing.</summary>
    public static RectTransform NextDeviceTool(bool needPc, bool needSwitch, bool needRouter)
    {
        if (needPc) return DeviceTool(CompoundToolKind.PC);
        if (needSwitch) return DeviceTool(CompoundToolKind.Switch);
        if (needRouter) return DeviceTool(CompoundToolKind.Router);
        return null;
    }

    public static RectTransform PingButton() =>
        NetworkUIManager.Instance != null ? NetworkUIManager.Instance.PingButtonRect : null;

    /// <summary>Send Packet — or Run Network, once there are packets waiting to go.</summary>
    public static RectTransform PacketButton()
    {
        var ui = NetworkUIManager.Instance;
        if (ui == null) return null;
        var send = PacketSendManager.Instance;
        return send != null && send.HasPendingWork ? ui.RunNetworkButtonRect : ui.SendPacketButtonRect;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static bool IsCabled(NetworkDevice d)
    {
        for (int i = 0; i < d.PortCount; i++)
            if (d.GetPortCable(i) != null) return true;
        return false;
    }

    static bool IsCabledTo(NetworkDevice d, NetworkDevice to)
    {
        for (int i = 0; i < d.PortCount; i++)
        {
            var cable = d.GetPortCable(i);
            if (cable != null && (cable.DeviceA == to || cable.DeviceB == to)) return true;
        }
        return false;
    }

    static DeviceAddress AddressOf(NetworkDevice device)
    {
        if (device == null) return null;
        var addr = device.GetComponent<DeviceAddress>();
        return addr != null && addr.HasAddress ? addr : null;
    }

    // The Router's ports that have an address (shut down or not — the subnet
    // is what's been decided).
    static IEnumerable<RouterInterfaceConfig.PortConfig> AddressedPorts(NetworkDevice router)
    {
        var config = router != null ? router.GetComponent<RouterInterfaceConfig>() : null;
        if (config == null) yield break;
        for (int i = 0; i < config.Count; i++)
        {
            var port = config.Get(i);
            if (port != null && SubnetMath.IsDottedQuad(port.ip) && SubnetMath.IsDottedQuad(port.mask)) yield return port;
        }
    }

    static bool HasAddressedPort(NetworkDevice router)
    {
        foreach (var _ in AddressedPorts(router)) return true;
        return false;
    }

    static bool RouterOnSubnet(NetworkDevice router, DeviceAddress a)
    {
        foreach (var port in AddressedPorts(router))
            if (SubnetMath.SameSubnet(a.Ip, a.Mask, port.ip, port.mask)) return true;
        return false;
    }

    static NetworkDevice SubnetMate(NetworkDevice pc, DeviceAddress a, IReadOnlyList<NetworkDevice> pcs)
    {
        foreach (var other in pcs)
        {
            if (other == pc) continue;
            var b = AddressOf(other);
            if (b != null && SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) return other;
        }
        return null;
    }
}
