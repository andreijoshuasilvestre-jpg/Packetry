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
/// never touches it), so it's the simplest possible guided flow: address one
/// PC, address the other onto the SAME subnet, then ping between them to
/// prove they can talk directly through the Switch. No Router exists in
/// this level — Level3Guide picks up from here to teach bridging different
/// subnets. Same lock/callout mechanism as Level3Guide (TutorialUIManager
/// callout/highlight + SidebarButtonLock dim/lock), just with fewer stages
/// and nothing to place beyond the pre-built table/PCs/Switch.
/// </summary>
public class Level2Guide : MonoBehaviour
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
    }

    // Long enough for PingPacketEffect's full journey — source condense wipe,
    // the orb's cable-to-cable travel (PC -> Switch -> PC here, two hops),
    // and the destination's forward wipe + explosion — to actually finish
    // playing before the completion panel takes over the screen.
    const float PingCelebrationDelay = 2.6f;

    readonly List<NetworkDevice> placedPCs = new List<NetworkDevice>();
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
        HideUnneededButtons();

        // Seed with whatever PCs already exist when this level starts —
        // Level2SceneSetup's pre-placed PCs (or TopologyLoader restoring a
        // previous save) both build in Awake(), earlier than this Start().
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
            if (device.deviceType == DeviceType.PC) placedPCs.Add(device);

        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted += HandlePingCompleted;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.OnTasksChanged += HandleTasksChanged;

            // Registration order == guide stage order (see BuildStages).
            tm.RegisterTask("Give the first PC an IP address", () => placedPCs.Count(p => GetAddress(p) != null) >= 1);
            tm.RegisterTask("Give the second PC an IP address on the SAME subnet", TwoPcsOnSameSubnet);
            tm.RegisterTask("Successfully ping between the two PCs", () => pingSucceeded);
        }

        EnterStage(0);
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
                     WorldMarkerDevice = FirstUnaddressedPc },

            // 1 — Address the second PC onto the SAME subnet. Reuses the
            // same "first still-unaddressed PC" marker lookup as stage 0 —
            // it naturally resolves to whichever PC is left, regardless of
            // FindObjectsByType's ordering.
            new() { Text = "Right-click the other PC and give it an IP on the SAME subnet (same network portion, e.g. 192.168.1.11) so the two can talk directly through the Switch.",
                     WorldMarker = FirstUnaddressedPcPosition,
                     WorldMarkerDevice = FirstUnaddressedPc },

            // 2 — Ping between them
            new() { Text = "Click <b>Ping</b>, then pick the two PCs to confirm they can reach each other.",
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

        bool inRange = stages != null && index >= 0 && index < stages.Count;
        var stage = inRange ? stages[index] : null;

        var ui = NetworkUIManager.Instance;
        if (ui != null)
            SidebarButtonLock.SetLocked(ui.PingButton, stage == null || !stage.UnlockPing);

        if (stage == null)
        {
            // All 3 objectives done — HandleTasksChanged hands off to
            // LevelCompletePanel; nothing left to point at.
            TutorialUIManager.Instance?.Hide();
            return;
        }

        TutorialUIManager.Instance?.Show(stage.Text, stage.Target?.Invoke(), buttonLabel: null);

        TutorialUIManager.Instance?.HideWorldMarkers();
        Vector3? marker = stage.WorldMarker?.Invoke();
        if (marker.HasValue) TutorialUIManager.Instance?.ShowWorldMarker(0, marker.Value, stage.WorldMarkerDevice?.Invoke());
    }

    // ── Stage advance + completion ───────────────────────────────────────────
    // Rides directly on TaskManager's own IsComplete tracking rather than
    // re-evaluating the same Func<bool> conditions a second time here —
    // registration order in InitNextFrame matches stage order in BuildStages,
    // so "N tasks complete" IS "we're on stage N".

    void HandleTasksChanged()
    {
        var tasks = TaskManager.Instance.Tasks;
        int completed = tasks.Count(t => t.IsComplete);
        if (completed != stageIndex) EnterStage(completed);

        if (completionHandled) return;
        if (tasks.Count == 0 || completed < tasks.Count) return;

        completionHandled = true;
        TutorialUIManager.Instance?.Hide();

        SaveManager.MarkLevelCompleted(2);
        SaveManager.SetStars(2, TaskManager.Instance.ComputeStars());
        AutoSaveManager.Instance?.SaveNow();

        StartCoroutine(ShowCompletePanelAfterPingPlays());
    }

    // Task completion fires the instant HandlePingCompleted sets
    // pingSucceeded — same frame as PingToolManager.RunPing's cable pulse and
    // device bounce (see PacketDeviceEffect.Play). Popping the panel up
    // immediately would cover that feedback before the player ever sees it,
    // so hold off for a beat and let the ping's own visuals actually play.
    IEnumerator ShowCompletePanelAfterPingPlays()
    {
        yield return new WaitForSeconds(PingCelebrationDelay);

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "Next up: Routers & Subnetting.",
            "Back to Level Select",
            () =>
            {
                MainMenuBootstrap.OpenLevelSelectOnLoad = true;
                LoadingScreenManager.LoadScene("MainMenu", "LOADING LEVEL SELECT");
            },
            TaskManager.Instance.ComputeStars());
    }

    // No Router ever exists in this level (see HideUnneededButtons), so any
    // successful ping between the two PCs is necessarily a direct
    // same-subnet path through the Switch — no need to reason about
    // ReachabilityService.PingResult.Path the way Level3Guide does for its
    // "crossed a router" check.
    void HandlePingCompleted(NetworkDevice source, NetworkDevice dest, ReachabilityService.PingResult result)
    {
        if (result.Success) pingSucceeded = true;
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

    Vector3? FirstUnaddressedPcPosition()
    {
        var pc = FirstUnaddressedPc();
        return pc != null ? pc.VisualTopPoint() : (Vector3?)null;
    }

    NetworkDevice FirstUnaddressedPc() => placedPCs.FirstOrDefault(p => p != null && GetAddress(p) == null);

    static DeviceAddress GetAddress(NetworkDevice device)
    {
        if (device == null) return null;
        var addr = device.GetComponent<DeviceAddress>();
        return addr != null && addr.HasAddress ? addr : null;
    }
}
