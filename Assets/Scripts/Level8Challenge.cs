using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 8's ("Free Build: Office LAN") objective provider. Only instantiated
/// by GameBootstrap.SetupLevelObjectives when LevelManager.CurrentLevel == 8 —
/// TaskManager itself stays level-agnostic, so this is just another provider
/// alongside Level5Challenge/Level6Challenge/Level7Challenge.
///
/// Unlike every earlier challenge, this one names no fixed device list or
/// shape — the checklist is written against a network *property* (every PC
/// can reach every other PC) rather than a prescribed topology, so the
/// player's own design choices satisfy it. The final task polls
/// ReachabilityService.AllPairsReachable directly every frame (via
/// TaskManager's own Update loop) instead of requiring the player to
/// manually Ping every pair — with a variable, player-chosen PC count there's
/// no fixed set of pairs to hand them as instructions the way Level5/6/7 do.
/// Same "blank canvas, no stage gating, no sidebar locking" style otherwise.
/// </summary>
public class Level8Challenge : MonoBehaviour, IKeepsFinishedBuild
{
    const int MinPcs = 3;

    // Live view of what's on the field — see PlacedDeviceTracker.
    readonly PlacedDeviceTracker placed = new PlacedDeviceTracker();
    IReadOnlyList<NetworkDevice> placedPCs => placed.PCs;
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
            tm.RegisterTask($"Place at least {MinPcs} PCs — add any Switches or a Router if your design needs them",
                () => placedPCs.Count >= MinPcs,
                hintNudge: "Drag PCs in from the sidebar — the shape of the network is entirely up to you.",
                hintSpecific: $"You need at least {MinPcs} PCs on the table before the rest of the checklist can pass.",
                hintAnswer: "Click the PC button in the sidebar three times.",
                specificGuide: new HintGuide { Button = () => HintTargets.NextDeviceTool(placedPCs.Count < MinPcs, false, false) });
            var uncabledPc = new HintGuide { Devices = () => (HintTargets.FirstUncabled(placedPCs), null) };
            tm.RegisterTask("Cable every PC into the network", AllPcsCabled,
                hintNudge: "Use the Cable tool — every PC needs at least one connection into whatever you've built.",
                hintSpecific: "Check each PC for a dangling, unconnected port.",
                hintAnswer: "Click Cable, click a PC, pick its port, then click whatever it should connect to.",
                specificGuide: uncabledPc,
                answerGuide: uncabledPc.WithButton(() => HintTargets.DeviceTool(CompoundToolKind.Cable)));
            tm.RegisterTask("Give every PC an IP address", AllPcsAddressed,
                hintNudge: "Right-click a PC to open its address panel.",
                hintSpecific: "Every PC needs both an IP and a subnet mask set — not just some of them.",
                hintAnswer: "e.g. 192.168.1.10 / 255.255.255.0 — reuse the same subnet for PCs that should talk directly.",
                specificGuide: new HintGuide { Devices = () => (HintTargets.FirstUnaddressed(placedPCs), null), Gesture = MouseGesture.RightClick });
            tm.RegisterTask("Get every PC talking to every other PC", () => ReachabilityService.AllPairsReachable(placedPCs),
                hintNudge: "Every PC should be able to ping every other PC — not just its nearest neighbor.",
                hintSpecific: "If two PCs are on different subnets with no Router bridging them, they can never reach each other.",
                hintAnswer: "Simplest fix: put every PC on the same subnet through Switches. Or bridge subnets with a Router the same way Level 6 did.",
                specificGuide: new HintGuide { Devices = () => HintTargets.FirstUnreachablePair(placedPCs) });
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
        if (placedPCs.Count < MinPcs) return false;
        foreach (var pc in placedPCs)
        {
            if (pc == null) return false;
            bool cabled = false;
            for (int i = 0; i < pc.PortCount; i++)
            {
                if (pc.GetPortCable(i) != null) { cabled = true; break; }
            }
            if (!cabled) return false;
        }
        return true;
    }

    bool AllPcsAddressed()
    {
        if (placedPCs.Count < MinPcs) return false;
        foreach (var pc in placedPCs)
        {
            var addr = pc == null ? null : pc.GetComponent<DeviceAddress>();
            if (addr == null || !addr.HasAddress) return false;
        }
        return true;
    }

    void HandleTasksChanged()
    {
        if (completionHandled) return;

        var tasks = TaskManager.Instance.Tasks;
        int completed = tasks.Count(t => t.IsComplete);
        if (tasks.Count == 0 || completed < tasks.Count) return;

        completionHandled = true;

        int stars = TaskManager.Instance.ComputeStars();
        SaveManager.MarkLevelCompleted(8);
        SaveManager.SetStars(8, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You designed an entire office LAN from a blank table and got every PC talking to every other PC — your call on the layout, start to finish.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }
}
