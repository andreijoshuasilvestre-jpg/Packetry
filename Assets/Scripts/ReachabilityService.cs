using System.Collections.Generic;

/// <summary>
/// Walks the physical Port/Cable adjacency graph (NetworkDevice.GetPortCable /
/// GetPortIndexForCable) to decide whether a ping between two addressed PCs
/// would actually succeed. Deliberately shallow, matching SubnetMath: switches
/// are subnet-unaware L2 flooders, and at most one router hop is modeled (its
/// own directly-connected interfaces only — no static routes / multi-router
/// forwarding, which is explicitly deferred future work).
/// </summary>
public static class ReachabilityService
{
    public struct PingResult
    {
        public bool Success;
        public int HopCount;
        public List<Cable> Path;
        public string FailureReason;
    }

    class Node
    {
        public NetworkDevice Device;
        public string SegIp, SegMask; // subnet identity of the wire this device was reached on
        public List<Cable> Path;
        public int RouterHops;
        public int EntryPort; // meaningful only when Device.deviceType == Router
    }

    public static PingResult Ping(NetworkDevice source, NetworkDevice dest, Cable excludeCable = null)
    {
        if (source == null || dest == null)
            return Fail("Invalid device.");
        if (source == dest)
            return Fail("Source and destination are the same device.");

        var srcAddr = source.GetComponent<DeviceAddress>();
        var dstAddr = dest.GetComponent<DeviceAddress>();
        if (srcAddr == null || !srcAddr.HasAddress)
            return Fail("Source has no IP address configured.");
        if (dstAddr == null || !dstAddr.HasAddress)
            return Fail("Destination has no IP address configured.");

        var visited = new HashSet<NetworkDevice> { source };
        var queue = new Queue<Node>();
        queue.Enqueue(new Node
        {
            Device = source, SegIp = srcAddr.Ip, SegMask = srcAddr.Mask,
            Path = new List<Cable>(), RouterHops = 0, EntryPort = -1,
        });

        while (queue.Count > 0)
        {
            Node node = queue.Dequeue();

            if (node.Device == dest)
            {
                bool matches = SubnetMath.SameSubnet(node.SegIp, node.SegMask, dstAddr.Ip, dstAddr.Mask);
                return matches
                    ? new PingResult { Success = true, HopCount = node.Path.Count, Path = node.Path, FailureReason = null }
                    : Fail("Different subnets — no router bridges them, or the bridging router's interfaces aren't both correctly configured.");
            }

            if (node.Device.deviceType == DeviceType.Router)
            {
                ExpandRouter(node, visited, queue, excludeCable);
                continue; // a router's own node never re-expands as a plain device below
            }

            ExpandFlood(node, visited, queue, excludeCable);
        }

        bool physicallyConnected = IsPhysicallyConnected(source, dest);
        return Fail(physicallyConnected
            ? "Different subnets — no router bridges them, or the bridging router's interfaces aren't both correctly configured."
            : "No cable path connects these devices.");
    }

    // Switches (transparent) and PCs (single-port leaves) both just propagate
    // the current segment identity unchanged to whatever's on their other ports.
    static void ExpandFlood(Node node, HashSet<NetworkDevice> visited, Queue<Node> queue, Cable excludeCable)
    {
        for (int i = 0; i < node.Device.PortCount; i++)
        {
            Cable cable = node.Device.GetPortCable(i);
            if (cable == null || cable == excludeCable) continue;

            NetworkDevice neighbor = cable.DeviceA == node.Device ? cable.DeviceB : cable.DeviceA;
            if (neighbor == null || visited.Contains(neighbor)) continue;

            visited.Add(neighbor);
            var path = new List<Cable>(node.Path) { cable };
            queue.Enqueue(new Node
            {
                Device = neighbor, SegIp = node.SegIp, SegMask = node.SegMask,
                Path = path, RouterHops = node.RouterHops,
                EntryPort = neighbor.GetPortIndexForCable(cable),
            });
        }
    }

    // A router only forwards past itself if the interface the packet arrived on
    // is up and actually configured for the subnet it's plugged into; once
    // validated, it auto-forwards out any of its OTHER up/configured interfaces
    // (no route lookup — that's the "own directly-connected interfaces only"
    // scope). Capped to one router in the whole path.
    static void ExpandRouter(Node node, HashSet<NetworkDevice> visited, Queue<Node> queue, Cable excludeCable)
    {
        if (node.RouterHops >= 1) return; // second router hop is out of scope (static routing, deferred)
        if (node.EntryPort < 0) return;

        RouterCLI cli = CLIPanelManager.Instance != null ? CLIPanelManager.Instance.GetOrCreateSession(node.Device) : null;
        if (cli == null) return;

        string entryIp   = cli.GetInterfaceIp(node.EntryPort);
        string entryMask = cli.GetInterfaceMask(node.EntryPort);
        bool entryActive = node.Device.IsPortActive(node.EntryPort);
        if (!entryActive || !SubnetMath.SameSubnet(node.SegIp, node.SegMask, entryIp, entryMask))
            return; // wrong-subnet or down interface — this router can't be crossed from here

        for (int q = 0; q < node.Device.PortCount; q++)
        {
            if (q == node.EntryPort) continue;
            if (!node.Device.IsPortActive(q)) continue;

            string exitIp   = cli.GetInterfaceIp(q);
            string exitMask = cli.GetInterfaceMask(q);
            if (!SubnetMath.IsDottedQuad(exitIp) || !SubnetMath.IsDottedQuad(exitMask)) continue;

            Cable cable = node.Device.GetPortCable(q);
            if (cable == null || cable == excludeCable) continue;

            NetworkDevice neighbor = cable.DeviceA == node.Device ? cable.DeviceB : cable.DeviceA;
            if (neighbor == null || visited.Contains(neighbor)) continue;

            visited.Add(neighbor);
            var path = new List<Cable>(node.Path) { cable };
            queue.Enqueue(new Node
            {
                Device = neighbor, SegIp = exitIp, SegMask = exitMask,
                Path = path, RouterHops = node.RouterHops + 1,
                EntryPort = neighbor.GetPortIndexForCable(cable),
            });
        }
    }

    /// <summary>True if every pair of the given (addressed, wired) PCs can
    /// currently ping each other. Used by open-ended "any topology" challenge
    /// levels (see Level8Challenge) that check a network property instead of
    /// a prescribed shape.</summary>
    public static bool AllPairsReachable(IReadOnlyList<NetworkDevice> pcs)
    {
        if (pcs.Count < 2) return false;
        for (int i = 0; i < pcs.Count; i++)
            for (int j = i + 1; j < pcs.Count; j++)
                if (!Ping(pcs[i], pcs[j]).Success) return false;
        return true;
    }

    /// <summary>True if, for every cable in the network, removing just that one
    /// cable still leaves all PCs it didn't directly disconnect mutually
    /// reachable. A PC has exactly one port, so its own last-mile cable is
    /// never redundant by construction — cutting it is expected to drop that
    /// PC, not fail the check; what this actually verifies is that the
    /// switch/router backbone itself has no single point of failure. Used by
    /// Level10Challenge.</summary>
    public static bool SurvivesAnyCableCut(IReadOnlyList<NetworkDevice> pcs, IReadOnlyList<Cable> cables) =>
        cables.Count > 0 && FindCriticalCable(pcs, cables) == null;

    /// <summary>The first cable whose loss alone cuts PCs (other than its own
    /// ends) off from each other — the single point of failure
    /// SurvivesAnyCableCut looks for — or null when there's none.</summary>
    public static Cable FindCriticalCable(IReadOnlyList<NetworkDevice> pcs, IReadOnlyList<Cable> cables)
    {
        foreach (Cable cut in cables)
        {
            var remaining = new List<NetworkDevice>();
            foreach (var pc in pcs)
                if (cut.DeviceA != pc && cut.DeviceB != pc) remaining.Add(pc);

            for (int i = 0; i < remaining.Count; i++)
                for (int j = i + 1; j < remaining.Count; j++)
                    if (!Ping(remaining[i], remaining[j], cut).Success) return cut;
        }
        return null;
    }

    // Pure physical-adjacency BFS (ignores subnets/active state entirely) —
    // used only to pick a more specific FailureReason when the L3-aware walk
    // above fails: "wrong subnet" vs. "not wired together at all".
    static bool IsPhysicallyConnected(NetworkDevice source, NetworkDevice dest)
    {
        var visited = new HashSet<NetworkDevice> { source };
        var queue = new Queue<NetworkDevice>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            NetworkDevice device = queue.Dequeue();
            if (device == dest) return true;

            for (int i = 0; i < device.PortCount; i++)
            {
                Cable cable = device.GetPortCable(i);
                if (cable == null) continue;
                NetworkDevice neighbor = cable.DeviceA == device ? cable.DeviceB : cable.DeviceA;
                if (neighbor == null || visited.Contains(neighbor)) continue;
                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }
        return false;
    }

    static PingResult Fail(string reason) =>
        new PingResult { Success = false, HopCount = 0, Path = new List<Cable>(), FailureReason = reason };
}
