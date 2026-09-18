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
public class Level5Challenge : MonoBehaviour
{
    readonly List<NetworkDevice> placedPCs = new List<NetworkDevice>();
    NetworkDevice placedSwitch;
    bool pingSucceeded;
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

        // Seed with whatever devices already exist when this level starts —
        // a restored save (TopologyLoader, built in Awake, earlier than this
        // Start) counts toward objectives the same as anything placed fresh.
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.deviceType == DeviceType.PC) placedPCs.Add(device);
            else if (device.deviceType == DeviceType.Switch && placedSwitch == null) placedSwitch = device;
        }

        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped += HandleDeviceDropped;
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted += HandlePingCompleted;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.OnTasksChanged += HandleTasksChanged;

            tm.RegisterTask("Place two PCs and a Switch", () => placedPCs.Count >= 2 && placedSwitch != null,
                hintNudge: "Drag devices in from the sidebar on the left.",
                hintSpecific: "You need exactly 2 PCs and 1 Switch on the table.",
                hintAnswer: "Click the PC button twice, then the Switch button once.");
            tm.RegisterTask("Cable both PCs to the Switch", BothPcsCabled,
                hintNudge: "Use the Cable tool to connect two devices' ports together.",
                hintSpecific: "Each PC needs its own cable running to the Switch — not to each other.",
                hintAnswer: "Click Cable, click a PC, pick its port, click the Switch, pick a free port. Repeat for the second PC.");
            tm.RegisterTask("Give both PCs IP addresses on the same subnet", TwoPcsOnSameSubnet,
                hintNudge: "Same subnet means matching network address — check both masks match too.",
                hintSpecific: "Try 192.168.1.x for both PCs, with mask 255.255.255.0.",
                hintAnswer: "e.g. 192.168.1.10 / 255.255.255.0 and 192.168.1.20 / 255.255.255.0.");
            tm.RegisterTask("Successfully ping between the two PCs", () => pingSucceeded,
                hintNudge: "Use the Ping tool from the sidebar, then pick your two PCs.",
                hintSpecific: "If it fails, double check both PCs actually show the same subnet color tint.",
                hintAnswer: "Click Ping, click the first PC, then the second PC.");
        }
    }

    void OnDestroy()
    {
        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped -= HandleDeviceDropped;
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted -= HandlePingCompleted;
        if (TaskManager.Instance != null) TaskManager.Instance.OnTasksChanged -= HandleTasksChanged;
    }

    void HandleDeviceDropped(NetworkDevice device, bool isNewDevice)
    {
        if (!isNewDevice) return;

        if (device.deviceType == DeviceType.PC) placedPCs.Add(device);
        else if (device.deviceType == DeviceType.Switch && placedSwitch == null) placedSwitch = device;
    }

    void HandlePingCompleted(NetworkDevice source, NetworkDevice dest, ReachabilityService.PingResult result)
    {
        if (result.Success && placedPCs.Contains(source) && placedPCs.Contains(dest)) pingSucceeded = true;
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
                MainMenuBootstrap.OpenLevelSelectOnLoad = true;
                LoadingScreenManager.LoadScene("MainMenu", "LOADING LEVEL SELECT");
            },
            stars);
    }
}
