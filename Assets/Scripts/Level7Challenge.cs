using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 7's ("Network Troubleshooting") objective provider. Only
/// instantiated by GameBootstrap.SetupLevelObjectives when
/// LevelManager.CurrentLevel == 7 — TaskManager itself stays level-agnostic,
/// so this is just another provider alongside Level5Challenge/Level6Challenge.
///
/// Unlike those two, the player isn't building anything from scratch —
/// Level7SceneSetup pre-places a full two-department network with three
/// seeded faults (an unplugged cable, a misaddressed PC, a shut-down router
/// interface — see Level7SceneSetup's own doc comment) and the checklist
/// here tracks the player finding and fixing each one, using only tools
/// every earlier level already taught (right-click re-address, the Router
/// CLI, the Ping tool's failure messages). Same "blank checklist, no stage
/// gating, no sidebar locking" style as Level5Challenge/Level6Challenge —
/// the difference is what the player starts with, not how they're guided.
/// </summary>
public class Level7Challenge : MonoBehaviour, IKeepsFinishedBuild
{
    // Live view of what's on the field — see PlacedDeviceTracker.
    readonly PlacedDeviceTracker placed = new PlacedDeviceTracker();
    IReadOnlyList<NetworkDevice> placedPCs => placed.PCs;
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
    // Level5Challenge.InitNextFrame. Also lets Level7SceneSetup's own
    // Awake() (which runs earlier in the same GameBootstrap.Awake(), see
    // Level7SceneSetup's doc comment) finish placing every device first.
    IEnumerator InitNextFrame()
    {
        yield return null;
        while (!GameBootstrap.IntroSequenceReady) yield return null;

        placed.Attach();
        packetCoverage.Attach();
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted += HandlePingCompleted;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.RegisterTask("Reconnect the disconnected PC", AllPcsCabled,
                hintNudge: "Look for a PC with nothing plugged into it — or ping between PCs and watch for a \"no cable path\" failure.",
                hintSpecific: "It's in the 192.168.10.x department: one of its two PCs isn't cabled to the Switch.",
                hintAnswer: "Click Cable, click the unplugged PC, pick its port, then click the Switch its neighbor is plugged into and pick a free port.",
                specificGuide: new HintGuide { Devices = () => (HintTargets.FirstUncabled(placedPCs), null) },
                answerGuide: new HintGuide
                {
                    Devices = () =>
                    {
                        var pc = HintTargets.FirstUncabled(placedPCs);
                        return (pc, HintTargets.SubnetMateUplink(pc, placedPCs));
                    },
                    Button = () => HintTargets.DeviceTool(CompoundToolKind.Cable),
                });
            tm.RegisterTask("Bring the Router's second interface back online", RouterHasTwoActiveInterfaces,
                hintNudge: "A Router port can have the right address and still be switched off. Ask the Router what state its ports are in.",
                hintSpecific: "Right-click the Router, type <b>enable</b>, then <b>show ip interface brief</b> — one port reads administratively down.",
                hintAnswer: "In the CLI: <b>enable</b>, <b>configure terminal</b>, <b>interface eth1</b>, <b>no shutdown</b>.",
                specificGuide: new HintGuide { Devices = () => (placedRouter, null), Gesture = MouseGesture.RightClick });
            tm.RegisterTask("Fix the misaddressed PC", TwoSubnetsOfTwo,
                hintNudge: "PCs on the same Switch must share a subnet. Compare each PC's address with its neighbor's.",
                hintSpecific: "One PC in the 192.168.20.x department has an address on a different subnet entirely.",
                hintAnswer: "Right-click the PC at 192.168.99.11 and change it to 192.168.20.11, mask 255.255.255.0.",
                specificGuide: new HintGuide { Devices = () => (HintTargets.FirstMisaddressed(placedPCs, placedRouter), null), Gesture = MouseGesture.RightClick });
            var pingPair = new HintGuide { Devices = () => HintTargets.SameSubnetPair(placedPCs) };
            tm.RegisterTask("Successfully ping between the two PCs in the affected department", () => sameSubnetPingSucceeded,
                hintNudge: "Once the address is fixed, prove it: ping from one PC in that department to the other.",
                hintSpecific: "Click Ping, then click the source PC and the destination PC — both should be on 192.168.20.x.",
                hintAnswer: "Click Ping, click the PC you just fixed, then click its neighbor on the same Switch.",
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
        return cabled >= placedPCs.Count && placedPCs.Count >= 4;
    }

    bool RouterHasTwoActiveInterfaces()
    {
        if (placedRouter == null) return false;
        return Enumerable.Range(0, placedRouter.PortCount).Count(i => placedRouter.IsPortActive(i)) >= 2;
    }

    // Requires at least two distinct subnets among the addressed PCs, each
    // shared by at least two PCs — i.e. two two-PC departments. Identical
    // shape to Level6Challenge.TwoSubnetsOfTwo: "fixed" and "originally
    // correct" converge on the same check.
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
        SaveManager.MarkLevelCompleted(7);
        SaveManager.SetStars(7, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You diagnosed and fixed every fault in a broken network — a cable, a router interface, and a misconfigured address — all on your own.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }
}
