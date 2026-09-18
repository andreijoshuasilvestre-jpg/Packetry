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
public class Level6Challenge : MonoBehaviour
{
    readonly List<NetworkDevice> placedPCs = new List<NetworkDevice>();
    readonly List<NetworkDevice> placedSwitches = new List<NetworkDevice>();
    NetworkDevice placedRouter;
    bool pingSucceeded;
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

        // Seed with whatever devices already exist when this level starts —
        // a restored save (TopologyLoader, built in Awake, earlier than this
        // Start) counts toward objectives the same as anything placed fresh.
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.deviceType == DeviceType.PC) placedPCs.Add(device);
            else if (device.deviceType == DeviceType.Switch) placedSwitches.Add(device);
            else if (device.deviceType == DeviceType.Router && placedRouter == null) placedRouter = device;
        }

        if (DragDropManager.Instance != null) DragDropManager.Instance.OnDeviceDropped += HandleDeviceDropped;
        if (PingToolManager.Instance != null) PingToolManager.Instance.OnPingCompleted += HandlePingCompleted;

        var tm = TaskManager.Instance;
        if (tm != null)
        {
            tm.OnTasksChanged += HandleTasksChanged;

            tm.RegisterTask("Place four PCs, two Switches, and a Router",
                () => placedPCs.Count >= 4 && placedSwitches.Count >= 2 && placedRouter != null);
            tm.RegisterTask("Cable two PCs to each Switch, and both Switches to the Router",
                () => AllPcsCabled() && SwitchesLinkedToRouter());
            tm.RegisterTask("Give each department's PCs matching IP addresses — one subnet per Switch",
                TwoSubnetsOfTwo);
            tm.RegisterTask("Configure both router interfaces with IP addresses and enable them",
                () => placedRouter != null && Enumerable.Range(0, placedRouter.PortCount).Count(i => placedRouter.IsPortActive(i)) >= 2);
            tm.RegisterTask("Successfully ping between two PCs in different departments, across the Router",
                () => pingSucceeded);
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
        else if (device.deviceType == DeviceType.Switch) placedSwitches.Add(device);
        else if (device.deviceType == DeviceType.Router && placedRouter == null) placedRouter = device;
    }

    void HandlePingCompleted(NetworkDevice source, NetworkDevice dest, ReachabilityService.PingResult result)
    {
        if (!result.Success || !placedPCs.Contains(source) || !placedPCs.Contains(dest)) return;

        var a = GetAddress(source);
        var b = GetAddress(dest);
        if (a == null || b == null) return;

        // Only a cross-subnet ping (i.e. one that actually crossed the
        // Router) satisfies this level — a same-subnet ping was already the
        // whole point of Level 5, so it shouldn't complete this one too.
        if (!SubnetMath.SameSubnet(a.Ip, a.Mask, b.Ip, b.Mask)) pingSucceeded = true;
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
                MainMenuBootstrap.OpenLevelSelectOnLoad = true;
                LoadingScreenManager.LoadScene("MainMenu", "LOADING LEVEL SELECT");
            },
            stars);
    }
}
