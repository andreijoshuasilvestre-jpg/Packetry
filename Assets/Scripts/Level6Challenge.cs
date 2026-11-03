using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 6's ("Two-LAN Challenge") objective provider. Only instantiated by
/// GameBootstrap.SetupLevelObjectives when LevelManager.CurrentLevel == 6 —
/// TaskManager itself stays level-agnostic, so this is just another provider
/// alongside Level2Guide/Level3Guide/Level4Guide/Level5Challenge.
///
/// The second unguided challenge: no stage gating, no sidebar locking, no
/// TutorialUIManager callouts — same "blank canvas, checklist-only" style as
/// Level5Challenge. Where Level 5 tested a single-subnet LAN (2 PCs + a
/// Switch) in isolation, this scales that up to two "departments" (2 PCs +
/// a Switch each) bridged by a Router — recapping Level 4's router/subnet
/// lesson at Level 5's build-it-yourself pace. Deliberately capped at two
/// subnets: ReachabilityService.ExpandRouter only ever allows a single
/// router hop per ping, so there is no payoff (and no way to actually
/// simulate) a third hop even though the Router model has a spare port.
/// </summary>
public class Level6Challenge : MonoBehaviour, IKeepsFinishedBuild
{
    // Live view of what's on the field — see PlacedDeviceTracker.
    readonly PlacedDeviceTracker placed = new PlacedDeviceTracker();
    IReadOnlyList<NetworkDevice> placedPCs => placed.PCs;
    IReadOnlyList<NetworkDevice> placedSwitches => placed.Switches;
    NetworkDevice placedRouter => placed.FirstRouter;
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

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.RegisterTask("Place four PCs, two Switches, and a Router",
                () => placedPCs.Count >= 4 && placedSwitches.Count >= 2 && placedRouter != null,
                hintNudge: "Picture two departments — two PCs and a Switch each — with one Router in the middle joining them.",
                hintSpecific: "PC, Switch and Router share one sidebar button — click the arrow beside it (or right-click it) to pick the others.",
                hintAnswer: "Drag out four PCs, then pick Switch from the arrow and drag out two, then pick Router and drag out one.",
                specificGuide: new HintGuide { Button = () => HintTargets.NextDeviceTool(placedPCs.Count < 4, placedSwitches.Count < 2, placedRouter == null) });
            var nextCable = new HintGuide { Devices = () => HintTargets.NextDepartmentCable(placedPCs, placedSwitches, placedRouter) };
            tm.RegisterTask("Cable two PCs to each Switch, and both Switches to the Router",
                () => AllPcsCabled() && SwitchesLinkedToRouter(),
                hintNudge: "Wire each department on its own first: its two PCs plug into their own Switch.",
                hintSpecific: "Then each Switch needs one more cable, to the Router — the PCs never cable to the Router directly.",
                hintAnswer: "Click Cable, click a PC, pick a port, click its Switch, pick a port. Repeat for all four PCs, then cable each Switch to a free Router port.",
                specificGuide: nextCable,
                answerGuide: nextCable.WithButton(() => HintTargets.DeviceTool(CompoundToolKind.Cable)));
            // Router before PCs, the order Level 4 taught: each department's
            // subnet is set on its Router port, then its PCs are addressed to match.
            tm.RegisterTask("Configure both router interfaces with IP addresses and enable them",
                () => placedRouter != null && Enumerable.Range(0, placedRouter.PortCount).Count(i => placedRouter.IsPortActive(i)) >= 2,
                hintNudge: "Give each department its own subnet. The Router needs one address in each — on the port cabled to that department's Switch.",
                hintSpecific: "Right-click the Router to open its CLI. Ports start shut down, so after addressing one you also have to turn it on with <b>no shutdown</b>.",
                hintAnswer: "Type <b>enable</b>, <b>configure terminal</b>, <b>interface eth0</b>, <b>ip address 192.168.1.1 255.255.255.0</b>, <b>no shutdown</b> — then the same for eth1 with 192.168.2.1. Match each port to the subnet of the Switch it's cabled to.",
                specificGuide: new HintGuide { Devices = () => (placedRouter, null), Gesture = MouseGesture.RightClick });
            tm.RegisterTask("Give each department's PCs matching IP addresses — one subnet per Switch",
                TwoSubnetsOfTwo,
                hintNudge: "Each PC goes on the same subnet as the Router port its Switch is cabled to.",
                hintSpecific: "Right-click each PC to address it. Use the same mask as its Router port, and change only the last number.",
                hintAnswer: "e.g. 192.168.1.10 and 192.168.1.11 on the Switch cabled to 192.168.1.1, 192.168.2.10 and 192.168.2.11 on the other, all with mask 255.255.255.0.",
                specificGuide: new HintGuide { Devices = () => (HintTargets.FirstMisaddressed(placedPCs, placedRouter), null), Gesture = MouseGesture.RightClick });
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
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
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
        return cabled >= 4;
    }

    bool SwitchesLinkedToRouter()
    {
        if (placedRouter == null || placedSwitches.Count < 2) return false;

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
        return linked >= 2;
    }

    // Requires at least two distinct subnets among the addressed PCs, each
    // shared by at least two PCs — i.e. two two-PC "departments".
    bool TwoSubnetsOfTwo()
    {
        var groups = new Dictionary<uint, int>();
        foreach (var pc in placedPCs)
        {
            var addr = GetAddress(pc);
            if (addr == null) continue;

            uint network = SubnetMath.NetworkAddress(addr.Ip, addr.Mask);
            groups[network] = groups.GetValueOrDefault(network) + 1;
        }
        return groups.Values.Count(count => count >= 2) >= 2;
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
        SaveManager.MarkLevelCompleted(6);
        SaveManager.SetStars(6, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You connected two departments into one working network — subnetting, switching, and routing, all on your own.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }
}
