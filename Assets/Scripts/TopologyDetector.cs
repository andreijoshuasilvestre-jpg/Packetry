using System.Collections.Generic;
using System.Linq;
using System.Text;

public enum TopologyShape { Link, Star, Bus, Ring, Tree, Mesh, PartialMesh }

public struct TopologySegment
{
    public TopologyShape Shape;
    public int DeviceCount;
    public int CableCount;
}

public struct TopologyResult
{
    public List<TopologySegment> Segments;
    public int UnconnectedDeviceCount;
    public string Summary;
}

/// <summary>
/// Classifies the shape(s) of the player-built device/cable graph — star, bus,
/// ring, tree, or mesh — from nothing but port degree and connectivity, the
/// same Port/Cable adjacency ReachabilityService walks for ping simulation.
/// Not wired into any level's tasks or win condition; exists for
/// TopologyDebugOverlay (spawned only in LevelManager.DevSandboxLevel) so the
/// detection itself can be tried out hands-on before deciding whether/how to
/// surface it to real players.
///
/// A PC has exactly one fixed port (see DeviceModelBuilder.BuildPC), so a PC
/// can never have degree > 1 — mechanically it can only ever be a leaf, or one
/// half of a bare two-device link, never part of a ring/bus/mesh backbone.
/// That distinction falls straight out of the degree math below; no
/// PC-specific special case is needed.
///
/// This is a heuristic for tinkering, not a rigorous graph-theory classifier —
/// a few shapes are genuinely ambiguous (a 3-node tree is simultaneously a
/// valid "star of 2 leaves" and a "3-node bus"; Star is checked first so that
/// case reads as Star) and anything with redundant links that isn't a clean
/// ring or full mesh falls into PartialMesh as a catch-all.
/// </summary>
public static class TopologyDetector
{
    public static TopologyResult Classify(IEnumerable<NetworkDevice> devices)
    {
        var all = devices.Where(d => d != null && !d.isPaletteItem).ToList();
        var adjacency = BuildAdjacency(all);

        var visited = new HashSet<NetworkDevice>();
        var segments = new List<TopologySegment>();
        int unconnected = 0;

        foreach (var device in all)
        {
            if (visited.Contains(device)) continue;
            var component = CollectComponent(device, adjacency, visited);

            if (component.Count == 1) { unconnected++; continue; }

            int edgeCount = component.Sum(d => adjacency[d].Count) / 2;
            segments.Add(new TopologySegment
            {
                Shape       = ClassifyComponent(component, adjacency, edgeCount),
                DeviceCount = component.Count,
                CableCount  = edgeCount,
            });
        }

        return new TopologyResult
        {
            Segments               = segments,
            UnconnectedDeviceCount = unconnected,
            Summary                = BuildSummary(segments, unconnected),
        };
    }

    // Mirrors ReachabilityService.IsPhysicallyConnected's walk (GetPortCable /
    // Cable.DeviceA-DeviceB), but builds full adjacency lists up front instead
    // of BFS-ing toward one target.
    static Dictionary<NetworkDevice, List<NetworkDevice>> BuildAdjacency(List<NetworkDevice> all)
    {
        var adjacency = all.ToDictionary(d => d, d => new List<NetworkDevice>());
        var known = new HashSet<NetworkDevice>(all);

        foreach (var device in all)
        {
            for (int i = 0; i < device.PortCount; i++)
            {
                Cable cable = device.GetPortCable(i);
                if (cable == null) continue;

                NetworkDevice neighbor = cable.DeviceA == device ? cable.DeviceB : cable.DeviceA;
                if (neighbor == null || !known.Contains(neighbor)) continue;

                adjacency[device].Add(neighbor);
            }
        }
        return adjacency;
    }

    static List<NetworkDevice> CollectComponent(NetworkDevice start,
        Dictionary<NetworkDevice, List<NetworkDevice>> adjacency, HashSet<NetworkDevice> visited)
    {
        var component = new List<NetworkDevice>();
        var queue = new Queue<NetworkDevice>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            NetworkDevice node = queue.Dequeue();
            component.Add(node);
            foreach (var neighbor in adjacency[node])
            {
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }
        return component;
    }

    static TopologyShape ClassifyComponent(List<NetworkDevice> component,
        Dictionary<NetworkDevice, List<NetworkDevice>> adjacency, int edgeCount)
    {
        int n = component.Count;
        if (n == 2) return TopologyShape.Link;

        int fullMeshEdges = n * (n - 1) / 2;
        if (edgeCount == fullMeshEdges) return TopologyShape.Mesh;

        var degrees = component.Select(d => adjacency[d].Count).ToList();
        int maxDegree = degrees.Max();

        if (edgeCount > n - 1)
        {
            bool everyNodeDegreeTwo = degrees.All(deg => deg == 2);
            return (edgeCount == n && everyNodeDegreeTwo) ? TopologyShape.Ring : TopologyShape.PartialMesh;
        }

        // Acyclic (edgeCount == n - 1): a single hub, a straight chain, or a
        // branching tree — checked in that priority order.
        if (maxDegree == n - 1) return TopologyShape.Star;
        return degrees.All(deg => deg <= 2) ? TopologyShape.Bus : TopologyShape.Tree;
    }

    static string BuildSummary(List<TopologySegment> segments, int unconnected)
    {
        var sb = new StringBuilder();

        if (segments.Count == 0)
        {
            sb.Append(unconnected > 0 ? "No connections yet" : "No devices placed yet");
        }
        else if (segments.Count == 1)
        {
            var s = segments[0];
            sb.Append($"{Label(s.Shape)}  ({s.DeviceCount} devices, {s.CableCount} cables)");
        }
        else
        {
            sb.Append($"{segments.Count} separate segments: ");
            sb.Append(string.Join(", ", segments.Select(s => $"{Label(s.Shape)} ({s.DeviceCount})")));
        }

        if (unconnected > 0)
            sb.Append($"   +{unconnected} unconnected");

        return sb.ToString();
    }

    static string Label(TopologyShape shape) => shape switch
    {
        TopologyShape.Link        => "Point-to-Point Link",
        TopologyShape.Star        => "Star",
        TopologyShape.Bus         => "Bus / Chain",
        TopologyShape.Ring        => "Ring",
        TopologyShape.Tree        => "Tree",
        TopologyShape.Mesh        => "Full Mesh",
        TopologyShape.PartialMesh => "Partial Mesh",
        _                         => "Unknown",
    };
}
