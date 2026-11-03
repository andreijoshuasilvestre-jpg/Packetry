using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 5's ("LAN Challenge") objective provider. Only instantiated by
/// GameBootstrap.SetupLevelObjectives when LevelManager.CurrentLevel == 5 —
/// TaskManager itself stays level-agnostic, so this is just another provider
/// alongside Level2Guide/Level3Guide/Level4Guide.
///
/// Unlike those, this is the first level with no guided walkthrough: no
/// stage gating, no sidebar locking, no TutorialUIManager callouts. The
/// sidebar starts fully unlocked and the play area starts blank — the point
/// of a "challenge" level is that the player decides what to place and in
/// what order, using only TaskUIManager's checklist (built automatically by
/// GameBootstrap.SetupUI since level 5 isn't excluded like 2/3/4 are) as a
/// guide. Deliberately scoped to a single-subnet LAN (2 PCs + a Switch) so
/// this first challenge tests independence, not new networking concepts.
/// </summary>
public class Level5Challenge : MonoBehaviour, IKeepsFinishedBuild
{
    // Live view of what's on the field — see PlacedDeviceTracker.
    readonly PlacedDeviceTracker placed = new PlacedDeviceTracker();
    IReadOnlyList<NetworkDevice> placedPCs => placed.PCs;
    NetworkDevice placedSwitch => placed.FirstSwitch;
    readonly PacketCoverageTracker packetCoverage = new PacketCoverageTracker();
    bool completionHandled;

    void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    // Deferred one frame so NetworkUIManager/TaskUIManager have finished
    // building before anything here reacts to them — same reasoning as
    // Level4Guide.InitNextFrame.
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
            var nextDevice = new HintGuide { Button = () => HintTargets.NextDeviceTool(placedPCs.Count < 2, placedSwitch == null, false) };
            tm.RegisterTask("Place two PCs and a Switch", () => placedPCs.Count >= 2 && placedSwitch != null,
                hintNudge: "Drag devices in from the sidebar on the left.",
                hintSpecific: "You need exactly 2 PCs and 1 Switch on the table.",
                hintAnswer: "Click the PC button twice, then the Switch button once.",
                specificGuide: nextDevice);
            var uncabledPc = new HintGuide { Devices = () => (HintTargets.FirstUncabled(placedPCs), placedSwitch) };
            tm.RegisterTask("Cable both PCs to the Switch", BothPcsCabled,
                hintNudge: "Use the Cable tool to connect two devices' ports together.",
                hintSpecific: "Each PC needs its own cable running to the Switch — not to each other.",
                hintAnswer: "Click Cable, click a PC, pick its port, click the Switch, pick a free port. Repeat for the second PC.",
                specificGuide: uncabledPc,
                answerGuide: uncabledPc.WithButton(() => HintTargets.DeviceTool(CompoundToolKind.Cable)));
            tm.RegisterTask("Give both PCs IP addresses on the same subnet", TwoPcsOnSameSubnet,
                hintNudge: "Same subnet means matching network address — check both masks match too.",
                hintSpecific: "Try 192.168.1.x for both PCs, with mask 255.255.255.0.",
                hintAnswer: "e.g. 192.168.1.10 / 255.255.255.0 and 192.168.1.20 / 255.255.255.0.",
                specificGuide: new HintGuide { Devices = () => (HintTargets.FirstMisaddressed(placedPCs), null), Gesture = MouseGesture.RightClick });
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

    bool BothPcsCabled()
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
        return cabled >= 2;
    }

    bool TwoPcsOnSameSubnet()
    {
        for (int i = 0; i < placedPCs.Count; i++)
        {
            var a = GetAddress(placedPCs[i]);
            if (a == null) continue;

            for (int j = i + 1; j < placedPCs.Count; j++)
            {
                var b = GetAddress(placedPCs[j]);
                if (b == null) continue;

                if (SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) return true;
            }
        }
        return false;
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
        SaveManager.MarkLevelCompleted(5);
        SaveManager.SetStars(5, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You built and verified a small LAN entirely on your own. More challenges are on the way.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            stars);
    }
}
