using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cosmetic subnet color-coding for Switches and Cables — reuses DeviceAddress's
/// "PC tint" language (GameTheme.SubnetTint), so a mismatched device reads as
/// "not part of this network" at a glance:
///   - A Switch takes the color of the subnet most of its network is on — the
///     PCs and Router interfaces plugged into it, or into any Switch linked to
///     it (switches pass through, a Router is a boundary, same as
///     ReachabilityService's own walk), one vote each. A tie for the most
///     leaves it uncolored: with nothing outnumbering anything there's no
///     telling which side is the mistake.
///   - A cable into a PC or Router carries that device's own subnet color (plain
///     if it has no address yet), so a PC on the wrong subnet shows up with a
///     cable that doesn't match the Switch it's plugged into.
///   - A cable between two Switches carries their network's color; a direct
///     cable between two addressed devices, their shared subnet's (plain when
///     they disagree — the same tie).
///
/// Decided purely by what's configured right now, never by the order it was
/// typed in. It used to be "the first address applied on the network wins",
/// so changing a correct PC's subnet and changing it back handed the network
/// to whichever device was actually wrong — the PC monitors' warning (see
/// IsOutOfStep) moved onto the correct PC, while ping, which never used that
/// rule, still behaved correctly. A subnet here is SubnetMath's Subnet —
/// network address and mask, the same pair ping compares.
///
/// Not a MonoBehaviour — RefreshAll() is called from the handful of places that
/// actually change subnet-relevant state (DeviceAddress.SetAddress,
/// RouterCLI.PushInterfaceState, CableManager's create/remove/register) rather
/// than polled every frame, since it walks the whole topology each time. Also
/// the single place SubnetColorRegistry gets told which subnets still exist.
/// </summary>
public static class SubnetVisualizer
{
    const string SwitchTintTargetPartName = "Switch_Body";
    const float  SwitchTintStrength       = 0.42f; // matches DeviceAddress.TintStrength

    static MaterialPropertyBlock s_switchTintBlock;

    /// <summary>Frees the color slots of subnets that no longer exist, then re-tints
    /// every Switch and Cable. PCs aren't re-tinted here: a PC's color only changes
    /// when its own address does, and DeviceAddress.SetAddress handles that itself
    /// (re-tinting every PC would also clear any selection highlight showing on one).</summary>
    public static void RefreshAll()
    {
        var devices = Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude);
        SubnetColorRegistry.Retain(CollectLiveSubnets(devices));

        var switchColors = new Dictionary<NetworkDevice, Color?>();
        foreach (var device in devices)
            if (device.deviceType == DeviceType.Switch && !switchColors.ContainsKey(device))
                ColorSwitchNetwork(device, switchColors);

        foreach (var kv in switchColors)
            ApplySwitchTint(kv.Key, kv.Value);

        foreach (var cable in Object.FindObjectsByType<Cable>(FindObjectsInactive.Exclude))
            cable.SetSubnetTint(CableColor(cable, switchColors));
    }

    /// <summary>RefreshAll plus every addressed PC — for when the palette itself
    /// changed (Settings > Subnet Colors), so every subnet-colored thing in the
    /// scene picks up the new color immediately.</summary>
    public static void RepaintAll()
    {
        RefreshAll();
        foreach (var addr in Object.FindObjectsByType<DeviceAddress>(FindObjectsInactive.Exclude))
            if (addr.HasAddress) addr.ApplyTint();
    }

    // Every subnet the player has configured anywhere right now — addressed PCs
    // plus every configured Router interface (cabled or not, so a subnet whose
    // only member is a Router interface keeps its slot while the player wires it).
    static HashSet<Subnet> CollectLiveSubnets(NetworkDevice[] devices)
    {
        var live = new HashSet<Subnet>();
        foreach (var device in devices)
        {
            var addr = device.GetComponent<DeviceAddress>();
            if (addr != null && addr.HasAddress)
                live.Add(SubnetMath.SubnetOf(addr.Ip, addr.Mask));

            var ifaces = device.GetComponent<RouterInterfaceConfig>();
            if (ifaces == null) continue;
            for (int i = 0; i < ifaces.Count; i++)
            {
                var iface = ifaces.Get(i);
                if (SubnetMath.IsDottedQuad(iface.ip) && SubnetMath.IsDottedQuad(iface.mask))
                    live.Add(SubnetMath.SubnetOf(iface.ip, iface.mask));
            }
        }
        return live;
    }

    /// <summary>Whether a device on subnet own is out of step with the network on
    /// the far side of one of its cables — outvoted there, or on a network tied
    /// with no subnet in the majority — and if so, the subnet to show it's at
    /// odds with. Built on the same vote RefreshAll colors that network's
    /// Switch/cables by, so PCMonitor's wrong-subnet warning always agrees with
    /// the colors on screen.</summary>
    public static bool IsOutOfStep(NetworkDevice device, Cable cable, Subnet own, out Subnet network)
    {
        network = default;
        if (device == null || cable == null) return false;
        NetworkDevice other = cable.DeviceA == device ? cable.DeviceB : cable.DeviceA;
        if (other == null) return false;

        var votes = other.deviceType == DeviceType.Switch
            ? TallySwitchNetwork(other, null)
            : TallyCable(cable);

        if (TryGetMajority(votes, out Subnet majority))
        {
            network = majority;
            return majority != own;
        }

        // A tie: nothing outnumbers anything, so every member is out of step —
        // shown against the strongest subnet that isn't its own.
        return TryGetStrongestOther(votes, own, out network);
    }

    // Gives every Switch on start's switched network that network's color
    // (null = nothing on it has an address yet, or its vote is tied).
    static void ColorSwitchNetwork(NetworkDevice start, Dictionary<NetworkDevice, Color?> switchColors)
    {
        var network = new List<NetworkDevice>();
        var votes   = TallySwitchNetwork(start, network);
        Color? color = TryGetMajority(votes, out Subnet majority) ? GameTheme.SubnetTint(majority) : (Color?)null;
        foreach (var sw in network) switchColors[sw] = color;
    }

    // Walks every Switch reachable from start over Switch-to-Switch cables (one
    // switched network, collected into network when it isn't null) and counts
    // one vote per PC and per Router interface plugged into it, for the subnet
    // each is on.
    static Dictionary<Subnet, int> TallySwitchNetwork(NetworkDevice start, List<NetworkDevice> network)
    {
        var votes = new Dictionary<Subnet, int>();
        var queue = new Queue<NetworkDevice>();
        var seen  = new HashSet<NetworkDevice> { start };
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            NetworkDevice sw = queue.Dequeue();
            network?.Add(sw);
            for (int p = 0; p < sw.PortCount; p++)
            {
                Cable cable = sw.GetPortCable(p);
                if (cable == null) continue;
                NetworkDevice neighbor = cable.DeviceA == sw ? cable.DeviceB : cable.DeviceA;
                if (neighbor == null) continue;

                if (neighbor.deviceType == DeviceType.Switch)
                {
                    if (seen.Add(neighbor)) queue.Enqueue(neighbor);
                    continue;
                }

                if (TryGetOwnSubnet(neighbor, neighbor.GetPortIndexForCable(cable), out Subnet subnet))
                    votes[subnet] = votes.GetValueOrDefault(subnet) + 1;
            }
        }

        return votes;
    }

    // Two addressed devices cabled directly — a two-member network, same vote.
    static Dictionary<Subnet, int> TallyCable(Cable cable)
    {
        var votes = new Dictionary<Subnet, int>();
        foreach (var end in new[] { cable.DeviceA, cable.DeviceB })
            if (end != null && TryGetOwnSubnet(end, end.GetPortIndexForCable(cable), out Subnet subnet))
                votes[subnet] = votes.GetValueOrDefault(subnet) + 1;
        return votes;
    }

    // The subnet with strictly more votes than any other — false if nothing
    // voted, or the top count is shared.
    static bool TryGetMajority(Dictionary<Subnet, int> votes, out Subnet majority)
    {
        majority = default;
        int best = 0;
        bool tied = false;
        foreach (var kv in votes)
        {
            if (kv.Value > best) { best = kv.Value; majority = kv.Key; tied = false; }
            else if (kv.Value == best) tied = true;
        }
        return best > 0 && !tied;
    }

    // The most-voted subnet other than own. Ties go to the lower network
    // address (then mask) — arbitrary, but stable, so the warning's colors
    // don't shuffle between polls.
    static bool TryGetStrongestOther(Dictionary<Subnet, int> votes, Subnet own, out Subnet strongest)
    {
        strongest = default;
        int best = 0;
        foreach (var kv in votes)
        {
            if (kv.Key == own) continue;
            bool better = kv.Value > best ||
                          (kv.Value == best && (kv.Key.Network < strongest.Network ||
                                                (kv.Key.Network == strongest.Network && kv.Key.Mask < strongest.Mask)));
            if (!better) continue;
            best = kv.Value;
            strongest = kv.Key;
        }
        return best > 0;
    }

    static Color? CableColor(Cable cable, Dictionary<NetworkDevice, Color?> switchColors)
    {
        NetworkDevice a = cable.DeviceA, b = cable.DeviceB;
        if (a == null || b == null) return null;

        bool aIsSwitch = a.deviceType == DeviceType.Switch;
        bool bIsSwitch = b.deviceType == DeviceType.Switch;

        if (aIsSwitch && bIsSwitch)
            return switchColors.TryGetValue(a, out Color? networkColor) ? networkColor : null;
        if (aIsSwitch) return OwnColor(b, cable);
        if (bIsSwitch) return OwnColor(a, cable);

        return TryGetMajority(TallyCable(cable), out Subnet shared) ? GameTheme.SubnetTint(shared) : (Color?)null;
    }

    static Color? OwnColor(NetworkDevice device, Cable cable) =>
        TryGetOwnSubnet(device, device.GetPortIndexForCable(cable), out Subnet subnet)
            ? GameTheme.SubnetTint(subnet) : (Color?)null;

    // The subnet a PC, or one Router interface, is itself on. False for a
    // Switch, an unaddressed PC, or a Router interface that's unconfigured or
    // shut down (ReachabilityService ignores a down interface too).
    static bool TryGetOwnSubnet(NetworkDevice device, int port, out Subnet subnet)
    {
        subnet = default;
        switch (device.deviceType)
        {
            case DeviceType.PC:
                var addr = device.GetComponent<DeviceAddress>();
                if (addr == null || !addr.HasAddress) return false;
                subnet = SubnetMath.SubnetOf(addr.Ip, addr.Mask);
                return true;

            case DeviceType.Router:
                if (port < 0 || !device.IsPortActive(port)) return false;
                var ifaces = device.GetComponent<RouterInterfaceConfig>();
                if (ifaces == null) return false;
                var iface = ifaces.Get(port);
                if (iface == null || !SubnetMath.IsDottedQuad(iface.ip) || !SubnetMath.IsDottedQuad(iface.mask))
                    return false;
                subnet = SubnetMath.SubnetOf(iface.ip, iface.mask);
                return true;
        }
        return false;
    }

    static void ApplySwitchTint(NetworkDevice sw, Color? tint)
    {
        s_switchTintBlock ??= new MaterialPropertyBlock();
        Renderer[] all = sw.GetComponentsInChildren<Renderer>();

        // Mirrors DeviceAddress.ApplyTint's own fallback: if nothing named
        // "Switch_Body" is found (an older save's Switch, or a same-session
        // procedural-fallback build predating a mesh rebuild), tint every
        // renderer rather than silently tinting nothing.
        bool hasNamedTarget = false;
        foreach (var r in all)
            if (r.gameObject.name == SwitchTintTargetPartName) { hasNamedTarget = true; break; }

        foreach (var r in all)
        {
            bool isTarget = !hasNamedTarget || r.gameObject.name == SwitchTintTargetPartName;
            if (!isTarget) continue;

            if (tint == null) { r.SetPropertyBlock(null); continue; }

            bool hasBase = r.sharedMaterial.HasProperty("_BaseColor");
            if (!hasBase && !r.sharedMaterial.HasProperty("_Color")) continue;
            string prop = hasBase ? "_BaseColor" : "_Color";
            Color orig = r.sharedMaterial.GetColor(prop);
            s_switchTintBlock.Clear();
            s_switchTintBlock.SetColor(prop, Color.Lerp(orig, tint.Value, SwitchTintStrength));
            r.SetPropertyBlock(s_switchTintBlock);
        }
    }
}
