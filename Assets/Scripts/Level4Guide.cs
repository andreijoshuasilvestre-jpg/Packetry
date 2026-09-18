using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Level 4's ("Routers &amp; Subnetting") guided walkthrough. Only instantiated
/// by GameBootstrap.SetupLevelObjectives when LevelManager.CurrentLevel == 4 —
/// TaskManager itself stays level-agnostic, so a future level just adds its
/// own equivalent of this component instead of branching inside TaskManager.
///
/// Picks up right where Level 3 (see Level3Guide) left off: the player
/// already knows enable -> configure terminal -> interface ethN -> end from
/// that level's pure CLI-navigation drill, so this level applies those same
/// moves to a real task — placing a Router, addressing two PCs onto
/// different subnets, then configuring the Router's two interfaces (with
/// CLIPanelManager's pulsing outline highlights pointing at the real
/// command each time) so the PCs can reach each other across it. No mode-
/// navigation hand-holding here; that lesson is already behind the player.
///
/// Locks the sidebar and forces step order — same idea as TutorialManager's
/// guided onboarding (Level 1), Level2Guide's addressing walkthrough, and
/// Level3Guide's CLI drill, reusing TutorialUIManager's callout/highlight
/// and SidebarButtonLock's dim/lock helpers rather than duplicating them.
/// Stages are revealed and gated one at a time, and stage advancement rides
/// directly on TaskManager's own IsComplete tracking (registration order
/// below == stage order) instead of re-polling the same conditions twice.
/// </summary>
public class Level4Guide : MonoBehaviour
{
    public static Level4Guide Instance { get; private set; }

    class GuideStage
    {
        public System.Func<string> Text;
        public bool UnlockRouter; // sidebar Router button clickable this stage
        public bool UnlockPing;   // sidebar Ping button clickable this stage
        public System.Func<RectTransform> Target;     // sidebar button to highlight, if any
        public System.Func<Vector3?> WorldMarker;      // world-space device to point at instead
        // Set alongside WorldMarker when it points at an already-placed
        // device, so its pulse becomes a translucent echo of that device's
        // own model instead of a generic circle.
        public System.Func<NetworkDevice> WorldMarkerDevice;

        // CLI-specific guidance: pulses an outline around the command to
        // type/click next (falls back to the input field itself if that
        // command isn't in the current mode's palette) plus a second outline
        // framing the prompt label, via CLIPanelManager.SetHighlightedCommand/
        // HighlightPrompt.
        public bool ShowCliHighlight;
        public string HighlightCommand;
    }

    NetworkDevice placedRouter;
    readonly List<NetworkDevice> placedPCs = new List<NetworkDevice>();
    bool pingSucceededAcrossRouter;
    bool completionHandled;

    List<GuideStage> stages;
    int stageIndex = -1;

    // Read by DragDropManager.StartDragging next to its existing
    // TutorialManager check — only the Router button is ever actually
    // clickable through this gate (every other placement button is hidden
    // outright, see HideUnneededButtons), so this only ever restricts Router.
    public bool IsDeviceAllowed(DeviceType t) =>
        t != DeviceType.Router || (stages != null && stageIndex >= 0 && stageIndex < stages.Count && stages[stageIndex].UnlockRouter);

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

        // Seed with whatever devices already exist when this level starts —
        // the Level4_Template scene's pre-placed PCs/Switch (via
        // TemplateTopologyLoader, or TopologyLoader restoring a previous
        // save) both build in Awake(), earlier than this Start(), so a
        // pre-built network segment counts toward objectives the same as
        // anything the player places afterward.
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.deviceType == DeviceType.PC) placedPCs.Add(device);
            else if (device.deviceType == DeviceType.Router && placedRouter == null) placedRouter = device;
        }

        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped += HandleDeviceDropped;
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted += HandlePingCompleted;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.OnTasksChanged += HandleTasksChanged;

            // Registration order == guide stage order (see BuildStages).
            tm.RegisterTask("Place a Router", () => placedRouter != null);
            tm.RegisterTask("Give two PCs IP addresses on different subnets", TwoPcsOnDifferentSubnets);
            tm.RegisterTask("Configure the first interface for the first PC's subnet",
                () => placedPCs.Count > 0 && InterfaceMatchesSubnet(placedPCs[0]));
            tm.RegisterTask("Configure the second interface for the second PC's subnet",
                () => placedPCs.Count > 1 && InterfaceMatchesSubnet(placedPCs[1]));
            tm.RegisterTask("Successfully ping between the two PCs across the router", () => pingSucceededAcrossRouter);
        }

        EnterStage(0);
    }

    void OnDestroy()
    {
        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped -= HandleDeviceDropped;
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted -= HandlePingCompleted;
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
    }

    // ── Guide stages ──────────────────────────────────────────────────────────

    void BuildStages()
    {
        stages = new List<GuideStage>
        {
            // 0 — Place a Router. World marker points at whichever table has
            // no devices on it yet (Level4_Template's reserved router table)
            // so the player sees exactly where to drop it, not just that the
            // sidebar button unlocked.
            new() { Text = () => "Place a Router to bridge the two subnets you're about to create.",
                     UnlockRouter = true,
                     Target = () => NetworkUIManager.Instance?.GetDeviceButtonRect(DeviceType.Router),
                     WorldMarker = () => FindRouterLandingPoint() },

            // 1 — Address the PCs onto different subnets. Not a sidebar
            // action (right-click a placed PC opens DeviceAddressPopup — see
            // DragDropManager.TryOpenDeviceConfig), so nothing is unlocked
            // here; a world marker points at the PC instead.
            new() { Text = () => "Right-click a PC to open its address popup, then give the two PCs IPs on different subnets (e.g. change one to 192.168.2.10).",
                     WorldMarker = () => placedPCs.Count > 0 && placedPCs[0] != null ? placedPCs[0].VisualTopPoint() : (Vector3?)null,
                     WorldMarkerDevice = () => placedPCs.Count > 0 ? placedPCs[0] : null },

            // 2 — Configure eth0 for the first PC's subnet, using the
            // player's actual chosen IP/mask rather than a generic example —
            // and reusing the exact moves taught in Level 3's CLI drill.
            new() { Text = () =>
                {
                    var addr = placedPCs.Count > 0 ? GetAddress(placedPCs[0]) : null;
                    string ipMask = addr != null ? $"{addr.Ip} {addr.Mask}" : "<ip> <mask>";
                    return $"Right-click the Router to open its CLI. Type <b>interface eth0</b>, then <b>ip address {ipMask}</b> and <b>no shutdown</b> to put this port on the same subnet as your first PC.";
                },
                     WorldMarker = () => placedRouter != null ? placedRouter.VisualTopPoint() : (Vector3?)null,
                     WorldMarkerDevice = () => placedRouter,
                     ShowCliHighlight = true, HighlightCommand = "ip address <ip> <mask>" },

            // 3 — eth1, same pattern for the second PC's subnet.
            new() { Text = () =>
                {
                    var addr = placedPCs.Count > 1 ? GetAddress(placedPCs[1]) : null;
                    string ipMask = addr != null ? $"{addr.Ip} {addr.Mask}" : "<ip> <mask>";
                    return $"Now do the same for the second subnet: <b>interface eth1</b>, then <b>ip address {ipMask}</b> and <b>no shutdown</b>.";
                },
                     WorldMarker = () => placedRouter != null ? placedRouter.VisualTopPoint() : (Vector3?)null,
                     WorldMarkerDevice = () => placedRouter,
                     ShowCliHighlight = true, HighlightCommand = "ip address <ip> <mask>" },

            // 4 — Ping across the router
            new() { Text = () => "Click <b>Ping</b>, then pick the two PCs to confirm they can reach each other through the Router.",
                     UnlockPing = true,
                     Target = () => NetworkUIManager.Instance?.PingButtonRect },
        };
    }

    // Nothing else on the sidebar is ever needed for this level's objectives
    // — the starting table/PCs/Switch are already placed and cabled by the
    // Level4_Template scene, so Table/PC/Switch/Cable have nothing left to
    // do, and Send Packet/Remove Cable/Remove Device aren't part of any
    // objective. Mirrors TutorialManager.HideUnneededButtons.
    void HideUnneededButtons()
    {
        var ui = NetworkUIManager.Instance;
        if (ui == null) return;

        SidebarButtonLock.SetActive(ui.GetDeviceButton(DeviceType.PC), false);
        SidebarButtonLock.SetActive(ui.GetDeviceButton(DeviceType.Switch), false);
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
        {
            SidebarButtonLock.SetLocked(ui.GetDeviceButton(DeviceType.Router), stage == null || !stage.UnlockRouter);
            SidebarButtonLock.SetLocked(ui.PingButton, stage == null || !stage.UnlockPing);
        }

        if (stage == null)
        {
            // All objectives done — HandleTasksChanged hands off to
            // LevelCompletePanel; nothing left to point at.
            TutorialUIManager.Instance?.Hide();
            CLIPanelManager.Instance?.ClearHighlights();
            return;
        }

        TutorialUIManager.Instance?.Show(stage.Text.Invoke(), stage.Target?.Invoke(), buttonLabel: null);

        TutorialUIManager.Instance?.HideWorldMarkers();
        Vector3? marker = stage.WorldMarker?.Invoke();
        if (marker.HasValue) TutorialUIManager.Instance?.ShowWorldMarker(0, marker.Value, stage.WorldMarkerDevice?.Invoke());

        if (stage.ShowCliHighlight)
        {
            CLIPanelManager.Instance?.SetHighlightedCommand(stage.HighlightCommand);
            CLIPanelManager.Instance?.HighlightPrompt(true);
        }
        else
        {
            CLIPanelManager.Instance?.ClearHighlights();
        }
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

        int stars = TaskManager.Instance.ComputeStars();
        SaveManager.MarkLevelCompleted(4);
        SaveManager.SetStars(4, stars);
        AutoSaveManager.Instance?.SaveNow();

        LevelCompletePanel.Instance?.Show(
            "Level Complete!",
            "You bridged two subnets with a router. More levels are on the way.",
            "Back to Level Select",
            () =>
            {
                MainMenuBootstrap.OpenLevelSelectOnLoad = true;
                LoadingScreenManager.LoadScene("MainMenu", "LOADING LEVEL SELECT");
            },
            stars);
    }

    void HandleDeviceDropped(NetworkDevice device, bool isNewDevice)
    {
        if (!isNewDevice) return;

        if (device.deviceType == DeviceType.Router)
        {
            if (placedRouter == null) placedRouter = device;
        }
        else if (device.deviceType == DeviceType.PC)
        {
            placedPCs.Add(device);
        }
    }

    // Path.Count >= 2 means the ping crossed at least one intermediate hop
    // (PC -> router, router -> PC) rather than a direct same-subnet link —
    // see ReachabilityService.ExpandRouter, which is the only thing that adds
    // extra cables to the path.
    void HandlePingCompleted(NetworkDevice source, NetworkDevice dest, ReachabilityService.PingResult result)
    {
        if (result.Success && result.Path.Count >= 2) pingSucceededAcrossRouter = true;
    }

    bool TwoPcsOnDifferentSubnets()
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

    // True once some active router interface shares a subnet with the given
    // PC's own address — used per-PC (stages 2/3) rather than "any two
    // interfaces differ from each other", so each stage's gate and callout
    // text can be tied to one specific, real PC.
    bool InterfaceMatchesSubnet(NetworkDevice pc)
    {
        var addr = GetAddress(pc);
        if (addr == null || placedRouter == null || CLIPanelManager.Instance == null) return false;

        RouterCLI cli = CLIPanelManager.Instance.GetOrCreateSession(placedRouter);
        for (int p = 0; p < placedRouter.PortCount; p++)
        {
            if (!placedRouter.IsPortActive(p)) continue;

            string ip = cli.GetInterfaceIp(p);
            string mask = cli.GetInterfaceMask(p);
            if (ip == null || mask == null) continue;

            if (SubnetMath.SameSubnet(ip, mask, addr.Ip, addr.Mask)) return true;
        }
        return false;
    }

    // Finds the table nothing is currently sitting on — the spot reserved for
    // the router — by checking occupancy rather than a hardcoded position or
    // size, so this keeps working if Level4_Template's tables are ever moved
    // or resized. Returns null (no marker shown) if every table is occupied,
    // e.g. on a replay where the router's already elsewhere.
    static Vector3? FindRouterLandingPoint()
    {
        var devices = Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude);
        foreach (var table in Table.AllTables)
        {
            bool occupied = false;
            foreach (var d in devices)
                if (table.ContainsXZ(d.transform.position)) { occupied = true; break; }

            if (!occupied) return table.VisualMarkerPoint();
        }
        return null;
    }

    static DeviceAddress GetAddress(NetworkDevice device)
    {
        if (device == null) return null;
        var addr = device.GetComponent<DeviceAddress>();
        return addr != null && addr.HasAddress ? addr : null;
    }
}
