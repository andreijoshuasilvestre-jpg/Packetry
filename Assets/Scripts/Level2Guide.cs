using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 2's ("IP Allocation") guided walkthrough. Only instantiated by
/// GameBootstrap.SetupLevelObjectives when LevelManager.CurrentLevel == 2 —
/// TaskManager itself stays level-agnostic, so each level just adds its own
/// equivalent of this component instead of branching inside TaskManager.
///
/// This is the player's first exposure to IP addressing at all (Level 1
/// never touches it), so the flow deliberately walks through the failure
/// case before the fix: address one PC, address the other onto a DIFFERENT
/// subnet on purpose, ping between them and watch it fail (no Router exists
/// in this level to bridge that gap — Level3Guide picks up from here to
/// teach bridging different subnets), then re-address the second PC onto the
/// SAME subnet and ping again to see it succeed. Same lock/callout mechanism
/// as Level3Guide (TutorialUIManager callout/highlight + SidebarButtonLock
/// dim/lock), just with nothing to place beyond the pre-built table/PCs/Switch.
/// </summary>
public class Level2Guide : MonoBehaviour, INarratedLevel
{
    public static Level2Guide Instance { get; private set; }

    class GuideStage
    {
        public string Text;
        public bool UnlockPing; // sidebar Ping button clickable this stage
        public System.Func<RectTransform> Target;     // sidebar button to highlight, if any
        public System.Func<Vector3?> WorldMarker;      // world-space device to point at instead
        // Set alongside WorldMarker when it points at an already-placed
        // device, so its pulse becomes a translucent echo of that device's
        // own model instead of a generic circle.
        public System.Func<NetworkDevice> WorldMarkerDevice;
        // What the marker's mouse acts out at that spot (see MouseGesture) —
        // RightClick where a device's popup/CLI opens that way.
        public MouseGesture MarkerGesture = MouseGesture.Click;
    }

    readonly List<NetworkDevice> placedPCs = new List<NetworkDevice>();
    // Captured the instant stage 1 begins (see EnterStage) — whichever PC is
    // still unaddressed right as stage 0's task completes is, by definition,
    // the one stage 1's instructions are about ("the other PC"), regardless
    // of which of the two physical PCs the player happened to address first.
    // Stage 3's marker/fix-check reads this instead of a fixed placedPCs
    // index, since "the second PC" isn't necessarily placedPCs[1].
    NetworkDevice secondPc;
    bool failedPingDemonstrated;
    bool pingSucceeded;
    bool completionHandled;

    List<GuideStage> stages;
    int stageIndex = -1;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    // NetworkUIManager builds its sidebar buttons in its own Start() (not
    // Awake()) — deferring one frame guarantees it has finished before this
    // reads button RectTransforms, same reasoning as TutorialManager.InitNextFrame.
    IEnumerator InitNextFrame()
    {
        yield return null;

        BuildStages();
        // Runs here — one frame after scene load, well before NetworkUIManager
        // actually queues its button reveal-flash (see StaggerRevealFlashChildren/
        // QueueRevealFlash, deferred two more frames past construction) — so a
        // tool this level hasn't unlocked yet is already inactive by the time
        // that queueing decision runs, instead of flashing in and then vanishing.
        HideUnneededButtons();

        // Seed with whatever PCs already exist when this level starts —
        // the Level2_Template scene's pre-placed PCs, or TopologyLoader
        // restoring a previous save. Waits for GameBootstrap to report the
        // starting topology in first: a template scene merges in a frame or
        // more after load, and scanning before then would find no PCs at all.
        while (!GameBootstrap.TopologyLoaded) yield return null;
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
            if (device.deviceType == DeviceType.PC) placedPCs.Add(device);

        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted += HandlePingCompleted;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.OnTasksChanged += HandleTasksChanged;

            // Registration order == guide stage order (see BuildStages), and
            // completed strictly in that order — see TaskManager.CompleteInOrder.
            tm.CompleteInOrder = true;
            tm.RegisterTask("Give the first PC an IP address", () => placedPCs.Count(p => GetAddress(p) != null) >= 1);
            tm.RegisterTask("Give the second PC an IP address on a DIFFERENT subnet", TwoPcsOnDifferentSubnet);
            tm.RegisterTask("Try pinging between them and see it fail", () => failedPingDemonstrated);
            tm.RegisterTask("Fix the second PC's address onto the SAME subnet as the first", TwoPcsOnSameSubnet);
            tm.RegisterTask("Successfully ping between the two PCs", () => pingSucceeded);
        }

        // Hold off on this level's first guide annotation until the whole
        // opening sequence has actually finished — see GameBootstrap.
        // IntroSequenceReady.
        while (!GameBootstrap.IntroSequenceReady) yield return null;

        // Not EnterStage(0) — a resumed save (or starting content that
        // already satisfies the first tasks) can put the level mid-guide.
        // Start on whichever stage is actually current.
        HandleTasksChanged();
    }

    void OnDestroy()
    {
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted -= HandlePingCompleted;
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
    }

    // ── Guide stages ──────────────────────────────────────────────────────────

    void BuildStages()
    {
        stages = new List<GuideStage>
        {
            // 0 — Address the first PC. Not a sidebar action — right-click a
            // placed PC opens DeviceAddressPopup (see
            // DragDropManager.TryOpenDeviceConfig) — so nothing is unlocked
            // here; a world marker points at whichever PC still needs one.
            new() { Text = "Right-click a PC to open its address popup, then give it an IP address and subnet mask (e.g. 192.168.1.10 / 255.255.255.0).",
                     WorldMarker = FirstUnaddressedPcPosition,
                     WorldMarkerDevice = FirstUnaddressedPc,
                     MarkerGesture = MouseGesture.RightClick },

            // 1 — Address the second PC onto a DIFFERENT subnet, on purpose.
            // Reuses the same "first still-unaddressed PC" marker lookup as
            // stage 0 — it naturally resolves to whichever PC is left,
            // regardless of FindObjectsByType's ordering. This is the setup
            // for the failure demonstration in stage 2, not the final state —
            // stage 3 has the player fix it once they've seen why it matters.
            new() { Text = "Right-click the other PC and give it an IP on a DIFFERENT subnet (different network portion, e.g. 192.168.2.11) — let's see what happens when two devices can't reach each other.",
                     WorldMarker = FirstUnaddressedPcPosition,
                     WorldMarkerDevice = FirstUnaddressedPc,
                     MarkerGesture = MouseGesture.RightClick },

            // 2 — Attempt the ping and watch it fail. Ping is unlocked here
            // (not just at the end) so the player can actually trigger
            // PingToolManager.RunPing's failure feedback — a red X materializes
            // over both devices (PingCheckmarkEffect via CrossIconBuilder, see
            // PingToolManager.RunPing) since there's no Router in this level to
            // bridge the mismatch.
            new() { Text = "Click <b>Ping</b>, then pick the two PCs — since they're on different subnets, the ping will fail. Watch what that failure looks like.",
                     UnlockPing = true,
                     Target = () => NetworkUIManager.Instance?.PingButtonRect },

            // 3 — Fix the second PC back onto the same subnet. Ping is
            // re-locked while this is pending (same "only unlock what this
            // stage needs" convention stages 0/1 already use) so the player's
            // attention goes to the address popup, not back to Ping.
            new() { Text = "Right-click the second PC again and change its address so it's on the SAME subnet as the first PC (e.g. 192.168.1.11).",
                     WorldMarker = PcNeedingSubnetFixPosition,
                     WorldMarkerDevice = PcNeedingSubnetFix,
                     MarkerGesture = MouseGesture.RightClick },

            // 4 — Ping again, now that they share a subnet
            new() { Text = "Click <b>Ping</b> again — now that both PCs share a subnet, they should reach each other.",
                     UnlockPing = true,
                     Target = () => NetworkUIManager.Instance?.PingButtonRect },
        };
    }

    // Nothing else on the sidebar is needed for this level — the starting
    // table/PCs/Switch are already placed and cabled by Level2SceneSetup,
    // and there's no Router in this level at all. Mirrors
    // TutorialManager.HideUnneededButtons / Level3Guide.HideUnneededButtons.
    void HideUnneededButtons()
    {
        // Ctrl+Z is a tool too — see UndoManager.AvailableInThisLevel.
        if (UndoManager.Instance != null) UndoManager.Instance.AvailableInThisLevel = false;

        var ui = NetworkUIManager.Instance;
        if (ui == null) return;

        SidebarButtonLock.SetActive(ui.GetDeviceButton(DeviceType.PC), false);
        SidebarButtonLock.SetActive(ui.GetDeviceButton(DeviceType.Switch), false);
        SidebarButtonLock.SetActive(ui.GetDeviceButton(DeviceType.Router), false);
        SidebarButtonLock.SetActive(ui.TableButton, false);
        SidebarButtonLock.SetActive(ui.CableButton, false);
        SidebarButtonLock.SetActive(ui.SendPacketButton, false);
        SidebarButtonLock.SetActive(ui.RemoveCableButton, false);
        SidebarButtonLock.SetActive(ui.RemoveDeviceButton, false);
    }

    void EnterStage(int index)
    {
        stageIndex = index;

        // First entry into stage 1 is the one moment "not yet addressed"
        // unambiguously means "the PC stage 1 is about" — capture it now
        // before the player's next click resolves it either way.
        if (index == 1 && secondPc == null) secondPc = FirstUnaddressedPc();
        // Resumed from a save past stage 1, so that moment never happened
        // this session — the PC stage 1 addressed is the one addressed last.
        else if (index > 1 && secondPc == null) secondPc = LastAddressedPc();

        bool inRange = stages != null && index >= 0 && index < stages.Count;
        var stage = inRange ? stages[index] : null;

        var ui = NetworkUIManager.Instance;
        if (ui != null)
            SidebarButtonLock.SetLocked(ui.PingButton, stage == null || !stage.UnlockPing);

        if (stage == null)
        {
            // All objectives done — HandleTasksChanged hands off to
            // LevelCompletePanel; nothing left to point at.
            TutorialUIManager.Instance?.Hide();
            return;
        }

        TutorialUIManager.Instance?.Show(stage.Text, stage.Target?.Invoke(), buttonLabel: null, progress: (index + 1, stages.Count));

        TutorialUIManager.Instance?.HideWorldMarkers();
        Vector3? marker = stage.WorldMarker?.Invoke();
        if (marker.HasValue) TutorialUIManager.Instance?.ShowWorldMarker(0, marker.Value, stage.WorldMarkerDevice?.Invoke(), stage.MarkerGesture);
    }

    // ── Stage advance + completion ───────────────────────────────────────────
    // Rides directly on TaskManager's own IsComplete tracking rather than
    // re-evaluating the same Func<bool> conditions a second time here —
    // registration order in InitNextFrame matches stage order in BuildStages,
    // so "N tasks complete" IS "we're on stage N".

    void HandleTasksChanged()
    {
        // Task registration/restore fires this long before the entrance has
        // finished — stage callouts wait for it, and InitNextFrame's own
        // post-intro call catches up to whatever stage is current by then.
        if (!GameBootstrap.IntroSequenceReady) return;

        var tasks = TaskManager.Instance.Tasks;
        int stage = TaskManager.Instance.CurrentTaskIndex;
        if (stage != stageIndex) EnterStage(stage);

        if (completionHandled) return;
        if (tasks.Count == 0 || stage < tasks.Count) return;

        completionHandled = true;
        TutorialUIManager.Instance?.Hide();

        SaveManager.MarkLevelCompleted(2);
        SaveManager.SetStars(2, TaskManager.Instance.ComputeStars());
        AutoSaveManager.Instance?.SaveNow();

        // Task completion fires the instant HandlePingCompleted sets
        // pingSucceeded — the same frame the ping's journey starts. The panel
        // itself waits for that journey to land (see LevelCompletePanel.Show).
        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "Next up: Routers & Subnetting.",
            "Back to Level Select",
            () =>
            {
                LoadingScreenManager.ExitToLevelSelect();
            },
            TaskManager.Instance.ComputeStars());
    }

    // No Router ever exists in this level (see HideUnneededButtons), so any
    // successful ping between the two PCs is necessarily a direct
    // same-subnet path through the Switch — no need to reason about
    // ReachabilityService.PingResult.Path the way Level3Guide does for its
    // "crossed a router" check. A failed ping here (stage 2, before the
    // player fixes the mismatch in stage 3) is just as informative as a
    // success — it's the whole point of that stage — so both outcomes latch
    // their own task the moment they're first observed (TaskManager.Update
    // never re-evaluates a task once IsComplete, so it doesn't matter that
    // "different subnet" stops being true once stage 3 fixes it).
    void HandlePingCompleted(NetworkDevice source, NetworkDevice dest, ReachabilityService.PingResult result)
    {
        if (result.Success) pingSucceeded = true;
        else failedPingDemonstrated = true;
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

    // Mirrors TwoPcsOnSameSubnet's pairwise scan but inverted — used by stage
    // 1's task to confirm the player actually created the mismatch on purpose
    // (both addressed, network portions differ) rather than just addressing
    // both PCs at all.
    bool TwoPcsOnDifferentSubnet()
    {
        for (int i = 0; i < placedPCs.Count; i++)
        {
            var a = GetAddress(placedPCs[i]);
            if (a == null) continue;

            for (int j = i + 1; j < placedPCs.Count; j++)
            {
                var b = GetAddress(placedPCs[j]);
                if (b == null) continue;

                if (!SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) return true;
            }
        }
        return false;
    }

    Vector3? FirstUnaddressedPcPosition()
    {
        var pc = FirstUnaddressedPc();
        return pc != null ? pc.VisualTopPoint() : (Vector3?)null;
    }

    NetworkDevice FirstUnaddressedPc() => placedPCs.FirstOrDefault(p => p != null && GetAddress(p) == null);

    NetworkDevice LastAddressedPc() => placedPCs
        .Where(p => GetAddress(p) != null)
        .OrderByDescending(p => GetAddress(p).AddressedAtTicks)
        .FirstOrDefault();

    // Stage 3's marker/highlight target — both PCs are addressed by now, so
    // "unaddressed" no longer picks anything out; this instead points at
    // secondPc (the one deliberately mismatched in stage 1) as long as it
    // still disagrees with the other PC's subnet — null once fixed, but by
    // then stage 3 is already behind us.
    Vector3? PcNeedingSubnetFixPosition()
    {
        var pc = PcNeedingSubnetFix();
        return pc != null ? pc.VisualTopPoint() : (Vector3?)null;
    }

    NetworkDevice PcNeedingSubnetFix()
    {
        if (secondPc == null) return null;
        var other = placedPCs.FirstOrDefault(p => p != null && p != secondPc);
        var a = GetAddress(secondPc);
        var b = other != null ? GetAddress(other) : null;
        if (a == null || b == null || SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) return null;
        return secondPc;
    }

    static DeviceAddress GetAddress(NetworkDevice device)
    {
        if (device == null) return null;
        var addr = device.GetComponent<DeviceAddress>();
        return addr != null && addr.HasAddress ? addr : null;
    }
}
