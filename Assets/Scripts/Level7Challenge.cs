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
public class Level7Challenge : MonoBehaviour
{
    readonly List<NetworkDevice> placedPCs = new List<NetworkDevice>();
    NetworkDevice placedRouter;
    bool sameSubnetPingSucceeded;
    bool crossSubnetPingSucceeded;
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

            tm.RegisterTask("Reconnect the disconnected PC", AllPcsCabled);
            tm.RegisterTask("Bring the Router's second interface back online", RouterHasTwoActiveInterfaces);
            tm.RegisterTask("Fix the misaddressed PC", TwoSubnetsOfTwo);
            tm.RegisterTask("Successfully ping between the two PCs in the affected department", () => sameSubnetPingSucceeded);
            tm.RegisterTask("Successfully ping between departments, across the Router", () => crossSubnetPingSucceeded);
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
        else if (device.deviceType == DeviceType.Router && placedRouter == null) placedRouter = device;
    }

    void HandlePingCompleted(NetworkDevice source, NetworkDevice dest, ReachabilityService.PingResult result)
    {
        if (!result.Success || !placedPCs.Contains(source) || !placedPCs.Contains(dest)) return;

        var a = GetAddress(source);
        var b = GetAddress(dest);
        if (a == null || b == null) return;

        if (SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) sameSubnetPingSucceeded = true;
        else crossSubnetPingSucceeded = true;
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
                MainMenuBootstrap.OpenLevelSelectOnLoad = true;
                LoadingScreenManager.LoadScene("MainMenu", "LOADING LEVEL SELECT");
            },
            stars);
    }
}
