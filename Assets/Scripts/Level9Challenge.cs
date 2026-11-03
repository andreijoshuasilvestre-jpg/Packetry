using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 9's ("Multi-Department Network") objective provider. Only
/// instantiated by GameBootstrap.SetupLevelObjectives when
/// LevelManager.CurrentLevel == 9 — TaskManager itself stays level-agnostic,
/// so this is just another provider alongside Level5Challenge/Level6Challenge
/// /Level7Challenge/Level8Challenge.
///
/// Scales Level6Challenge's two-department shape up to three: three switched
/// departments (2 PCs + a Switch each) all bridged through one Router. This
/// is the practical ceiling for a single router in this game — the Router
/// model has exactly 3 ports (see DeviceModelBuilder.BuildRouterProcedural),
/// so three departments uses every interface with none spare, and
/// ReachabilityService.ExpandRouter only ever allows one router hop per ping
/// regardless of how many of its ports are active. GameBootstrap gives this
/// level a larger play area than the default (see SetupPlayArea) since six
/// PCs, three Switches and a Router need more table space than Level 5/6/7's
/// smaller builds.
/// </summary>
public class Level9Challenge : MonoBehaviour, IKeepsFinishedBuild
{
    const int DepartmentCount = 3;
    const int PcsPerDepartment = 2;

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

    // Deferred one frame so NetworkUIManager/TaskUIManager have finished
    // building before anything here reacts to them — same reasoning as
    // Level5Challenge.InitNextFrame.
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
                      placedSwitches.Count >= DepartmentCount && placedRouter != null,
                hintNudge: "Three departments this time — two PCs and a Switch each — all joined by one Router.",
                hintSpecific: "PC, Switch and Router share one sidebar button — click the arrow beside it (or right-click it) to pick the others.",
                hintAnswer: "Drag out six PCs, then pick Switch from the arrow and drag out three, then pick Router and drag out one.",
                specificGuide: new HintGuide
                {
                    Button = () => HintTargets.NextDeviceTool(placedPCs.Count < DepartmentCount * PcsPerDepartment,
                        placedSwitches.Count < DepartmentCount, placedRouter == null),
                });
            var nextCable = new HintGuide { Devices = () => HintTargets.NextDepartmentCable(placedPCs, placedSwitches, placedRouter) };
            tm.RegisterTask("Cable two PCs to each Switch, and all three Switches to the Router",
                () => AllPcsCabled() && SwitchesLinkedToRouter(),
                hintNudge: "Wire each department on its own first: its two PCs plug into their own Switch.",
                hintSpecific: "Then each Switch gets one cable to the Router — three Router ports used, one per department.",
                hintAnswer: "Click Cable, click a PC, pick a port, click its Switch, pick a port. Repeat for all six PCs, then cable each Switch to a free Router port.",
                specificGuide: nextCable,
                answerGuide: nextCable.WithButton(() => HintTargets.DeviceTool(CompoundToolKind.Cable)));
            // Router before PCs, the order Level 4 taught: each department's
            // subnet is set on its Router port, then its PCs are addressed to match.
            tm.RegisterTask("Configure all three router interfaces with IP addresses and enable them",
                () => placedRouter != null && Enumerable.Range(0, placedRouter.PortCount).Count(i => placedRouter.IsPortActive(i)) >= DepartmentCount,
                hintNudge: "Give each department its own subnet. The Router needs one address in each, on the port leading to that department's Switch — and every port has to be turned on.",
                hintSpecific: "Right-click the Router. For each port: <b>interface</b> it, give it an <b>ip address</b>, then <b>no shutdown</b> — ports start shut down.",
                hintAnswer: "Type <b>enable</b>, <b>configure terminal</b>, <b>interface eth0</b>, <b>ip address 192.168.1.1 255.255.255.0</b>, <b>no shutdown</b>. Repeat for eth1 (192.168.2.1) and eth2 (192.168.3.1), matching each port to its Switch's subnet.",
                specificGuide: new HintGuide { Devices = () => (placedRouter, null), Gesture = MouseGesture.RightClick });
            tm.RegisterTask("Give each department's PCs matching IP addresses — one subnet per Switch, three departments total",
                ThreeSubnetsOfTwo,
                hintNudge: "Same rule as two departments, just one more: each PC goes on the subnet of the Router port its Switch is cabled to.",
                hintSpecific: "Right-click each PC to address it. Use the same mask as its Router port, and change only the last number.",
                hintAnswer: "e.g. 192.168.1.10 and 192.168.1.11 on the Switch cabled to 192.168.1.1, then 192.168.2.x and 192.168.3.x the same way, all with mask 255.255.255.0.",
                specificGuide: new HintGuide { Devices = () => (HintTargets.FirstMisaddressed(placedPCs, placedRouter), null), Gesture = MouseGesture.RightClick });
            var pingPair = new HintGuide { Devices = () => HintTargets.SameSubnetPair(placedPCs) };
            tm.RegisterTask("Successfully ping between two PCs within the same department",
                () => sameSubnetPingSucceeded,
                hintNudge: "Test one department at a time: ping between the two PCs on the same Switch.",
                hintSpecific: "Click Ping, then click a source PC and a destination PC that share a Switch and a subnet.",
                hintAnswer: "Click Ping, click one PC, then click the other PC on the same Switch.",
                specificGuide: pingPair,
                answerGuide: pingPair.WithButton(HintTargets.PingButton));
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

    // Requires at least three distinct subnets among the addressed PCs, each
    // shared by at least two PCs — i.e. three two-PC "departments". Same
    // shape as Level6Challenge.TwoSubnetsOfTwo, just scaled to three.
    bool ThreeSubnetsOfTwo()
    {
        var groups = new Dictionary<uint, int>();
        foreach (var pc in placedPCs)
        {
            var addr = GetAddress(pc);
            if (addr == null) continue;

            uint network = SubnetMath.NetworkAddress(addr.Ip, addr.Mask);
            groups[network] = groups.GetValueOrDefault(network) + 1;
        }
        return groups.Values.Count(count => count >= PcsPerDepartment) >= DepartmentCount;
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
        SaveManager.MarkLevelCompleted(9);
        SaveManager.SetStars(9, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You connected three departments into one working network entirely on your own — the Router's every port earning its keep.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }
}
