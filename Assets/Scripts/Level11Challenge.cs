using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

/// <summary>
/// Level 11's ("VLSM: Multi-Site Network") objective provider. Only
/// instantiated by GameBootstrap.SetupLevelObjectives when
/// LevelManager.CurrentLevel == 11 — TaskManager itself stays level-agnostic,
/// so this is just another provider alongside Level5-10Challenge.
///
/// Deliberately reuses Level9Challenge's exact physical shape — three
/// switched departments (2 PCs + a Switch each) bridged through one 3-port
/// Router — since the mechanics (placing, cabling, routing) are already
/// taught by this point and aren't what this level is testing. The one new
/// skill is addressing: instead of three independent, identically-sized
/// subnets, all three departments must be carved out of ONE shared block
/// (192.168.1.0/24), sized differently to fit three different stated host
/// requirements (4, 14, 30) — the actual VLSM skill SubnetMath's own class
/// comment calls out as deliberately unimplemented as a solver. This level
/// only ever checks a player-chosen allocation (via SubnetMath.RangesOverlap
/// / IsWithinSupernet, added alongside this level) — it never computes one
/// for them.
///
/// Shows a single ConceptCardManager explainer up front (the scenario and
/// the shared /24 it must fit inside), then behaves exactly like the other
/// challenge levels: no stage gating, no sidebar locking, just
/// TaskUIManager's checklist. Requirements are checked by SIZE (sorted
/// ascending, matched against the sorted requirement list) and by requiring
/// three distinct prefix lengths — not by which physical department ends up
/// biggest — so any valid VLSM split passes, not one specific prescribed
/// layout (same "check properties, not topology" philosophy as
/// Level8Challenge/Level9Challenge).
/// </summary>
public class Level11Challenge : MonoBehaviour, IKeepsFinishedBuild, INarratedLevel
{
    const int DepartmentCount = 3;
    const int PcsPerDepartment = 2;

    // The scenario's three stated host-count requirements, smallest to
    // largest — classic VLSM teaching numbers chosen so the exact minimal
    // answer is a clean /29 + /28 + /27 split of one /24 (30/14/4 hosts),
    // though ThreeVlsmSubnets() only requires "big enough for its own
    // requirement," not that exact minimum — a player who over-allocates
    // (e.g. uses a /26 where a /28 would do) still passes.
    static readonly int[] RequiredHostCounts = { 4, 14, 30 };

    const string SupernetIp   = "192.168.1.0";
    const string SupernetMask = "255.255.255.0";

    // Live view of what's on the field — see PlacedDeviceTracker.
    readonly PlacedDeviceTracker placed = new PlacedDeviceTracker();
    IReadOnlyList<NetworkDevice> placedPCs => placed.PCs;
    IReadOnlyList<NetworkDevice> placedSwitches => placed.Switches;
    NetworkDevice placedRouter => placed.FirstRouter;
    bool sameSubnetPingSucceeded;
    readonly PacketCoverageTracker packetCoverage = new PacketCoverageTracker();
    bool completionHandled;

    void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    // Deferred one frame so NetworkUIManager/TaskUIManager/ConceptCardManager
    // have finished building before anything here reacts to them — same
    // reasoning as Level9Challenge.InitNextFrame.
    IEnumerator InitNextFrame()
    {
        yield return null;
        while (!GameBootstrap.IntroSequenceReady) yield return null;

        // Seed with whatever devices already exist when this level starts —
        // a restored save (TopologyLoader, built in Awake, earlier than this
        // Start) counts toward objectives the same as anything placed fresh.
        placed.Attach();
        packetCoverage.Attach();
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted += HandlePingCompleted;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.RegisterTask("Place six PCs, three Switches, and a Router",
                () => placedPCs.Count >= DepartmentCount * PcsPerDepartment &&
                      placedSwitches.Count >= DepartmentCount && placedRouter != null);
            tm.RegisterTask("Cable two PCs to each Switch, and all three Switches to the Router",
                () => AllPcsCabled() && SwitchesLinkedToRouter());
            tm.RegisterTask(
                $"Address each department from the shared block {SupernetIp}/24 — sized for {RequiredHostCounts[0]}, {RequiredHostCounts[1]}, and {RequiredHostCounts[2]} hosts, with no two subnets overlapping",
                ThreeVlsmSubnets,
                hintNudge: "All three subnets have to come out of the same 192.168.1.0/24 block — different sizes, not three separate /24s like earlier levels.",
                hintSpecific: "Sort the three needs smallest to largest (4, 14, 30) and pick a mask for each that just fits: /29, /28, /27.",
                hintAnswer: "e.g. 192.168.1.0/27 (30 hosts), 192.168.1.32/28 (14 hosts), 192.168.1.48/29 (4 hosts).");
            tm.RegisterTask("Configure all three router interfaces with IP addresses and enable them",
                () => placedRouter != null && Enumerable.Range(0, placedRouter.PortCount).Count(i => placedRouter.IsPortActive(i)) >= DepartmentCount);
            tm.RegisterTask("Successfully ping between two PCs within the same department",
                () => sameSubnetPingSucceeded);
            packetCoverage.RegisterTask(tm, placedPCs);

            // Subscribed only once every task is in — a resumed save's tasks
            // register already complete one at a time, and the first alone
            // would otherwise read as the whole level done. A restored
            // finished build (see IKeepsFinishedBuild) arrives fully complete:
            // its completion and stars were recorded the first time round,
            // so the Level Complete panel isn't replayed.
            completionHandled = tm.Tasks.All(t => t.IsComplete);
            tm.OnTasksChanged += HandleTasksChanged;
        }

        ConceptCardManager.Instance?.Show(new ConceptCardManager.ConceptCard
        {
            Title = "Variable-Length Subnet Masking",
            Beats = new[]
            {
                "So far, every subnet you've made has used the same size mask for every department — one /24 each, no matter how many hosts it actually needed.",
                $"This time, all three departments have to share ONE block: <b>{SupernetIp}/24</b>. You'll carve it into three pieces instead of getting a fresh block each.",
                $"Each department needs a different amount of room — one needs space for {RequiredHostCounts[0]} hosts, one for {RequiredHostCounts[1]}, one for {RequiredHostCounts[2]}. Giving all three an identical mask would either waste addresses or not fit — that's what VLSM (Variable Length Subnet Masking) solves: sizing each piece to fit what it's actually for.",
                "Pick a mask for each department that's big enough for its own requirement, keep all three inside that one /24, and make sure none of their address ranges overlap."
            },
            BuildDiagram = BuildVlsmDiagram
        });
    }

    void OnDestroy()
    {
        placed.Detach();
        packetCoverage.Detach();
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted -= HandlePingCompleted;
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
    }

    void HandlePingCompleted(NetworkDevice source, NetworkDevice dest, ReachabilityService.PingResult result)
    {
        if (!result.Success || !placedPCs.Contains(source) || !placedPCs.Contains(dest)) return;

        var a = GetAddress(source);
        var b = GetAddress(dest);
        if (a == null || b == null) return;

        if (SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) sameSubnetPingSucceeded = true;
    }

    bool AllPcsCabled()
    {
        int cabled = 0;
        foreach (var pc in placedPCs)
        {
            if (pc == null) continue;
            for (int i = 0; i < pc.PortCount; i++)
            {
                if (pc.GetPortCable(i) != null) { cabled++; break; }
            }
        }
        return cabled >= DepartmentCount * PcsPerDepartment;
    }

    bool SwitchesLinkedToRouter()
    {
        if (placedRouter == null || placedSwitches.Count < DepartmentCount) return false;

        int linked = 0;
        foreach (var sw in placedSwitches)
        {
            if (sw == null) continue;
            for (int i = 0; i < sw.PortCount; i++)
            {
                var cable = sw.GetPortCable(i);
                if (cable == null) continue;
                if (cable.DeviceA == placedRouter || cable.DeviceB == placedRouter) { linked++; break; }
            }
        }
        return linked >= DepartmentCount;
    }

    // The real VLSM check. Groups addressed PCs into distinct subnets (same
    // idea as Level9Challenge.ThreeSubnetsOfTwo), then requires each of the
    // three groups' (ip, mask):
    //   1. lands entirely inside the shared 192.168.1.0/24 block,
    //   2. has a distinct prefix length from the other two (proves the
    //      player actually varied the mask instead of using one safe size
    //      for everything — the "Variable Length" part), and
    //   3. is big enough for its matched requirement once all three groups'
    //      usable-host counts and all three requirements are sorted
    //      ascending and compared pairwise (a bigger subnet always covers a
    //      smaller requirement, so sorting both and comparing index-by-index
    //      is equivalent to "does some valid assignment exist" — no need to
    //      pin a specific requirement to a specific physical department).
    // Finally, no two groups' ranges may overlap — checked independently of
    // grouping-by-exact-network-equality, since a /25 and an unrelated /27
    // can still overlap despite having different network addresses.
    bool ThreeVlsmSubnets()
    {
        var groups = new Dictionary<uint, (string ip, string mask, int count)>();
        foreach (var pc in placedPCs)
        {
            var addr = GetAddress(pc);
            if (addr == null) continue;

            uint network = SubnetMath.NetworkAddress(addr.Ip, addr.Mask);
            if (groups.TryGetValue(network, out var existing))
                groups[network] = (existing.ip, existing.mask, existing.count + 1);
            else
                groups[network] = (addr.Ip, addr.Mask, 1);
        }

        var subnets = groups.Values.Where(g => g.count >= PcsPerDepartment).ToList();
        if (subnets.Count < DepartmentCount) return false;

        // A player might address more than three distinct groups (e.g. while
        // experimenting) — only the three largest-by-usable-hosts count
        // toward the requirement match; any extra valid groups are ignored
        // rather than penalized.
        var byUsableHosts = subnets
            .Select(s => (s.ip, s.mask, prefix: SubnetMath.MaskToPrefixLength(s.mask)))
            .Where(s => s.prefix >= 0)
            .OrderBy(s => SubnetMath.UsableHostCount(s.prefix))
            .Take(DepartmentCount)
            .ToList();
        if (byUsableHosts.Count < DepartmentCount) return false;

        // Every chosen subnet must fit entirely inside the shared block.
        if (byUsableHosts.Any(s => !SubnetMath.IsWithinSupernet(s.ip, s.mask, SupernetIp, SupernetMask)))
            return false;

        // Three genuinely different sizes — the "variable length" part.
        if (byUsableHosts.Select(s => s.prefix).Distinct().Count() < DepartmentCount)
            return false;

        // Sorted ascending on both sides — smallest subnet must cover the
        // smallest requirement, and so on.
        for (int i = 0; i < DepartmentCount; i++)
            if (SubnetMath.UsableHostCount(byUsableHosts[i].prefix) < RequiredHostCounts[i])
                return false;

        // No two of the chosen subnets' address ranges may overlap.
        for (int i = 0; i < byUsableHosts.Count; i++)
            for (int j = i + 1; j < byUsableHosts.Count; j++)
                if (SubnetMath.RangesOverlap(byUsableHosts[i].ip, byUsableHosts[i].mask, byUsableHosts[j].ip, byUsableHosts[j].mask))
                    return false;

        return true;
    }

    static DeviceAddress GetAddress(NetworkDevice device)
    {
        if (device == null) return null;
        var addr = device.GetComponent<DeviceAddress>();
        return addr != null && addr.HasAddress ? addr : null;
    }

    void HandleTasksChanged()
    {
        if (completionHandled) return;

        var tasks = TaskManager.Instance.Tasks;
        int completed = tasks.Count(t => t.IsComplete);
        if (tasks.Count == 0 || completed < tasks.Count) return;

        completionHandled = true;

        int stars = TaskManager.Instance.ComputeStars();
        SaveManager.MarkLevelCompleted(11);
        SaveManager.SetStars(11, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You carved one shared address block into three right-sized pieces instead of wasting a fresh /24 on every department — that's VLSM.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }

    // Three plate nodes captioned with each requirement and the mask that
    // exactly fits it, connected left-to-right to read as "one shared block,
    // carved into three differently-sized pieces" — same shared node/arrow
    // vocabulary as Level4Guide's gateway diagram (see ConceptCardManager).
    static void BuildVlsmDiagram(Transform slot)
    {
        var font = Resources.Load<TMP_FontAsset>("Fonts/PixeloidMono SDF");

        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(slot, false);
        var headerTmp = header.AddComponent<TextMeshProUGUI>();
        headerTmp.font      = font;
        headerTmp.fontSize  = 15;
        headerTmp.alignment = TextAlignmentOptions.Top;
        headerTmp.color     = GameTheme.ElectricBlue;
        headerTmp.text      = $"Shared block: {SupernetIp}/24";
        var headerRt = header.GetComponent<RectTransform>();
        headerRt.anchorMin        = new Vector2(0.5f, 1f);
        headerRt.anchorMax        = new Vector2(0.5f, 1f);
        headerRt.pivot            = new Vector2(0.5f, 1f);
        headerRt.anchoredPosition = new Vector2(0f, -2f);
        headerRt.sizeDelta        = new Vector2(560f, 22f);

        Vector2 nodeSize = new Vector2(160f, 68f);
        float y = -22f;
        ConceptCardManager.BuildPlateNode(slot, font, $"{RequiredHostCounts[2]} hosts\n192.168.1.0 /27", new Vector2(-200f, y), nodeSize);
        ConceptCardManager.BuildArrow(slot, font, new Vector2(-100f, y));
        ConceptCardManager.BuildPlateNode(slot, font, $"{RequiredHostCounts[1]} hosts\n192.168.1.32 /28", new Vector2(0f, y), nodeSize);
        ConceptCardManager.BuildArrow(slot, font, new Vector2(100f, y));
        ConceptCardManager.BuildPlateNode(slot, font, $"{RequiredHostCounts[0]} hosts\n192.168.1.48 /29", new Vector2(200f, y), nodeSize);
    }
}
